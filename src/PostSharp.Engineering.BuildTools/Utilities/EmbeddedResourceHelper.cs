// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using System;
using System.Collections.Generic;
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

        ExtractResource( context, fileName, targetDirectory, replacements );
    }

    /// <summary>
    /// Gets the body of the <c>$products</c> array of <c>Orchestrator.ps1</c>: the directory of every source dependency
    /// of the product, in the order the dependencies are declared, and then the repository of the product itself.
    /// </summary>
    /// <remarks>
    /// The order is the declaration order because that is the order the products have to be built, bumped and deployed
    /// in: a source dependency is declared after the ones it consumes. The repository of the consolidated product comes
    /// last, because its own version is derived from the versions the other products have just been given.
    /// </remarks>
    private static string GetOrchestratedProducts( Product product )
    {
        var sourceDependenciesDirectory = product.SourceDependenciesDirectory.Replace( Path.DirectorySeparatorChar, '/' );

        var lines = product.SourceDependencies
            .Select( d => $"    \"$repo/{sourceDependenciesDirectory}/{d.Name}\"," )
            .Append( "    $repo" );

        return string.Join( Environment.NewLine, lines );
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