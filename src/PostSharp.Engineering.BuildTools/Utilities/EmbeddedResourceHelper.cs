// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Utilities;

internal static class EmbeddedResourceHelper
{
    public static void ExtractScript( BuildContext context, string fileName, string targetDirectory )
    {
        var product = context.Product;
        var replacements = new Dictionary<string, string>();
        replacements.Add( "<ENG_PATH>", product.EngineeringDirectory );

        // The token of every GitHub organization that the build reaches besides its own. These are derived from the
        // source dependencies rather than listed in EnvironmentVariableNames.All, because which organizations a build
        // reaches is a property of the product. TeamCitySettingsFile.CreateBuildScopedTokenSettings is what puts them
        // in the environment of the build, and without forwarding them the container would authenticate against every
        // repository with the token of the organization of the product.
        var ownOwner = (product.DependencyDefinition.VcsRepository as GitHubRepository)?.Owner;

        var foreignOrganizationTokens = product.SourceDependencies
            .Select( d => d.VcsRepository )
            .OfType<GitHubRepository>()
            .Where( r => !string.Equals( r.Owner, ownOwner, StringComparison.OrdinalIgnoreCase ) )
            .Select( r => GitHubRepository.GetTokenEnvironmentVariableName( r.Owner ) );

        // Combine standard environment variables with product-specific additional ones
        var allEnvironmentVariables = EnvironmentVariableNames.All
            .Concat( product.AdditionalDockerEnvironmentVariables )
            .Concat( foreignOrganizationTokens )
            .Distinct( StringComparer.Ordinal )
            .OrderBy( x => x );

        replacements.Add( "<ENVIRONMENT_VARIABLES>", string.Join( ",", allEnvironmentVariables ) );
        replacements.Add( "<PRODUCT_NAME>", product.ProductNameWithoutDot );
        replacements.Add( "<ORCHESTRATED_PRODUCTS>", GetOrchestratedProducts( product ) );

        // Image-name prefix for chained Dockerfiles: stems are "{prefix}-{layer}". Must match
        // ContainerRequirements.GetImagePrefix / the default DockerSpec.ImageName.
        replacements.Add( "<DOCKER_IMAGE_PREFIX>", $"{product.ProductNameWithoutDot}-{product.ProductFamily.Version}".ToLowerInvariant() );

        AddDockerTestReplacements( product, replacements );

        ExtractResource( context, fileName, targetDirectory, replacements );
    }

    /// <summary>
    /// Adds the directory that <c>RunDockerTests.ps1</c> carries: where the tests are, relative to the repository
    /// root.
    /// </summary>
    /// <remarks>
    /// That is a fact about the repository rather than a choice a caller makes, so the generated script holds it
    /// instead of taking it as an argument from a build configuration. One script is generated per repository, so the
    /// configurations cannot disagree about it, and a product that states two different values is refused rather than
    /// silently generating one of them. Where what the tests consume is, is not here: a test resolves that from where
    /// it lives, because it is a fact about the product rather than about this SDK.
    /// </remarks>
    private static void AddDockerTestReplacements( Product product, Dictionary<string, string> replacements )
    {
        var configurations = product.AdditionalCiBuildConfigurations
            .OfType<DockerTestsAdditionalCiBuildConfiguration>()
            .ToList();

        var paths = configurations.Select( c => c.Path ).Distinct( StringComparer.Ordinal ).ToList();

        if ( paths.Count > 1 )
        {
            throw new InvalidOperationException(
                "The Docker test configurations of this product declare different test directories, but one "
                + "RunDockerTests.ps1 is generated for the whole repository. Give every one of them the same path "
                + $"(found: {string.Join( ", ", paths )})." );
        }

        replacements.Add( "<DOCKER_TESTS_PATH>", paths.FirstOrDefault() ?? DockerTestsAdditionalCiBuildConfiguration.DefaultPath );
    }

    /// <summary>
    /// Gets the body of the <c>$products</c> array of <c>Orchestrator.ps1</c>: the directory of every source dependency
    /// of the product, in dependency order, and then the repository of the product itself.
    /// </summary>
    /// <remarks>
    /// The repository of the consolidated product comes last, because its own version is derived from the versions the
    /// other products have just been given.
    /// </remarks>
    private static string GetOrchestratedProducts( Product product )
    {
        var sourceDependenciesDirectory = product.SourceDependenciesDirectory.Replace( Path.DirectorySeparatorChar, '/' );

        var lines = GetBuildOrder( product.SourceDependencies )
            .Select( d => $"    \"$repo/{sourceDependenciesDirectory}/{d.Name}\"," )
            .Append( "    $repo" );

        return string.Join( Environment.NewLine, lines );
    }

    /// <summary>
    /// Orders <paramref name="definitions"/> so that a product comes after every product it depends on. The order the
    /// products are bumped and deployed in has to be this one: a product reads the versions of its dependencies, so a
    /// dependency bumped after its consumer leaves the consumer pinned to the previous version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The relation is the transitive one, over package dependencies and source dependencies alike, and it is computed
    /// over the whole graph rather than over <paramref name="definitions"/>: two products of the list can be ordered by
    /// a product that is not in it, and comparing only the direct dependencies would miss that.
    /// </para>
    /// <para>
    /// Products that no path connects keep the order they are declared in, so that the generated script changes only
    /// when the graph does.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The dependencies of the products form a cycle.</exception>
    internal static ImmutableArray<DependencyDefinition> GetBuildOrder( IReadOnlyList<DependencyDefinition> definitions )
    {
        var remaining = definitions.ToList();
        var ordered = ImmutableArray.CreateBuilder<DependencyDefinition>( definitions.Count );
        var emitted = new HashSet<DependencyDefinition>();

        // The members of the list that each member has to follow.
        var prerequisites = definitions.ToDictionary(
            d => d,
            d => GetReachable( d ).Intersect( definitions ).Where( r => r != d ).ToHashSet() );

        while ( remaining.Count > 0 )
        {
            // The first in declaration order whose prerequisites have all been emitted, so that the order of
            // unrelated products is the one they are declared in.
            var next = remaining.FirstOrDefault( d => prerequisites[d].IsSubsetOf( emitted ) );

            if ( next == null )
            {
                throw new InvalidOperationException(
                    "The dependencies of the following products form a cycle, so they cannot be built in any order: "
                    + string.Join( ", ", remaining.Select( d => d.Name ) ) + "." );
            }

            ordered.Add( next );
            emitted.Add( next );
            remaining.Remove( next );
        }

        return ordered.MoveToImmutable();
    }

    /// <summary>
    /// Gets every product that <paramref name="definition"/> reaches through its dependencies, directly or not.
    /// </summary>
    private static HashSet<DependencyDefinition> GetReachable( DependencyDefinition definition )
    {
        var reachable = new HashSet<DependencyDefinition>();
        var pending = new Stack<DependencyDefinition>();
        pending.Push( definition );

        while ( pending.Count > 0 )
        {
            var current = pending.Pop();

            // A dependency reached twice is not walked again, which also stops a cycle in the graph from looping here.
            // A cycle among the products being ordered is reported by GetBuildOrder instead.
            foreach ( var next in current.Dependencies.Select( d => d.Definition ).Concat( current.SourceDependencies ) )
            {
                if ( reachable.Add( next ) )
                {
                    pending.Push( next );
                }
            }
        }

        return reachable;
    }

    public static void ExtractResource(
        BuildContext context,
        string fileName,
        string targetDirectory,
        IReadOnlyDictionary<string, string>? replacements = null )
    {
        var targetPath = Path.Combine( context.RepoDirectory, targetDirectory, fileName );

        using var resource = typeof(EmbeddedResourceHelper).Assembly.GetManifestResourceStream( $"PostSharp.Engineering.BuildTools.Resources.{fileName}" )
                             ?? throw new InvalidOperationException( $"Cannot find the resource {fileName}." );

        using var reader = new StreamReader( resource );
        var text = reader.ReadToEnd();

        if ( replacements != null )
        {
            foreach ( var replacement in replacements )
            {
                text = text.Replace( replacement.Key, replacement.Value, StringComparison.Ordinal );
            }
        }

        TextFileHelper.WriteIfDifferent( targetPath, text, context );
    }
}