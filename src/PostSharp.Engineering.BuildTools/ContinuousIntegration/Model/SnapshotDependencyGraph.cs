// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

/// <summary>
/// Validates the dependencies that the build configurations of a product declare on one another.
/// </summary>
/// <remarks>
/// Every mistake reported here is otherwise found by TeamCity rather than by the generator, and the message TeamCity
/// gives names neither the declaration that caused it nor the file to edit: an unknown identifier becomes an
/// unresolved Kotlin reference, and a cycle makes the server reject the whole settings file. Validating before
/// anything is generated is what lets the error name the product definition instead.
/// </remarks>
internal static class SnapshotDependencyGraph
{
    private static readonly BuildConfiguration[] _configurations =
        [BuildConfiguration.Debug, BuildConfiguration.Release, BuildConfiguration.Public];

    /// <param name="configurations">The additional build configurations of the generated settings, which are those that the
    /// product declares unless <c>generate-scripts</c> creates more.</param>
    public static bool TryValidate( ConsoleHelper console, Product product, IReadOnlyList<AdditionalCiBuildConfiguration>? configurations = null )
    {
        configurations ??= product.AdditionalCiBuildConfigurations;

        var success = true;
        var configurationsById = new Dictionary<string, AdditionalCiBuildConfiguration>( StringComparer.Ordinal );

        foreach ( var additionalConfiguration in configurations )
        {
            if ( !configurationsById.TryAdd( additionalConfiguration.Id, additionalConfiguration ) )
            {
                console.WriteError(
                    $"The product declares several additional build configurations with the identifier '{additionalConfiguration.Id}'. "
                    + "The generated settings would hold two build types of that name." );

                success = false;
            }
        }

        // The edges of the graph, from the object name of a build type to the object names it depends on. Both kinds
        // of node are addressed the same way, because both become a Kotlin object in the same generated project.
        var edges = new Dictionary<string, List<string>>( StringComparer.Ordinal );

        foreach ( var additionalConfiguration in configurations )
        {
            success &= TryAddNode( additionalConfiguration, additionalConfiguration.Id, $"The '{additionalConfiguration.Id}' build configuration" );
        }

        foreach ( var configuration in _configurations )
        {
            var configurationInfo = product.Configurations[configuration];
            var customBuildConfiguration = configurationInfo.CustomBuildConfiguration;

            success &= TryValidateDeploymentDependencies( configuration, configurationInfo );

            if ( customBuildConfiguration == null )
            {
                // The stock build step runs 'Build.ps1 test', which refuses a configuration that does not support the build.
                if ( !configurationInfo.SupportsBuild && configurationInfo.ExportsToTeamCityBuild )
                {
                    console.WriteError(
                        $"The '{configuration}' build configuration does not support the build (SupportsBuild) but is exported to TeamCity "
                        + "without a CustomBuildConfiguration, so its generated build step would fail." );

                    success = false;
                }

                continue;
            }

            var description = $"The '{configuration}' build configuration";

            if ( !configurationInfo.ExportsToTeamCityBuild )
            {
                console.WriteError( $"{description} declares a custom build configuration but is not exported to TeamCity." );

                success = false;

                continue;
            }

            success &= TryValidateReplacement( customBuildConfiguration, configuration, configurationInfo, description );

            success &= TryAddNode( customBuildConfiguration, SnapshotDependency.GetBuildObjectName( configuration ), description );
        }

        return success & TryValidateCycles();

        // A deployment is no node of the graph: nothing depends on it, so it cannot close a cycle. Its targets must exist.
        bool TryValidateDeploymentDependencies( BuildConfiguration configuration, BuildConfigurationInfo configurationInfo )
        {
            var isValid = true;

            foreach ( var dependency in configurationInfo.DeploymentDependencies ?? [] )
            {
                if ( dependency.TryGetObjectName( product, configurations ) == null )
                {
                    console.WriteError(
                        $"The deployments of the '{configuration}' build configuration depend on '{dependency}', which is neither an "
                        + "additional build configuration of the product nor an exported build configuration." );

                    isValid = false;
                }

                // Without artifact rules the dependency is an ordering constraint, and reusing the last successful build removes
                // the ordering: TeamCity would emit neither block, and the deployment would not wait.
                if ( dependency.ArtifactRules is null or { Length: 0 } && dependency.ReuseLastSuccessfulBuild == true )
                {
                    console.WriteError(
                        $"The deployments of the '{configuration}' build configuration depend on '{dependency}' without artifact rules "
                        + "and reuse its last successful build, so they would not wait for it. Clear ReuseLastSuccessfulBuild." );

                    isValid = false;
                }
            }

            return isValid;
        }

        bool TryValidateReplacement(
            AdditionalCiBuildConfiguration customBuildConfiguration,
            BuildConfiguration configuration,
            BuildConfigurationInfo configurationInfo,
            string description )
        {
            var isValid = true;

            if ( configurationsById.ContainsKey( customBuildConfiguration.Id ) )
            {
                console.WriteError(
                    $"{description} is replaced by '{customBuildConfiguration.Id}', which is also listed in "
                    + "Product.AdditionalCiBuildConfigurations. The generated settings would hold that configuration twice, once "
                    + $"under its own identifier and once as '{SnapshotDependency.GetBuildObjectName( configuration )}'." );

                isValid = false;
            }

            if ( customBuildConfiguration.ArtifactRules != null )
            {
                console.WriteError(
                    $"{description} is replaced by a configuration that sets ArtifactRules, which the generator overrides: a "
                    + "build configuration publishes the artifact directories that the deployment reads from it by name. Declare "
                    + "the extra rules in BuildConfigurationInfo.AdditionalArtifactRules instead." );

                isValid = false;
            }

            if ( customBuildConfiguration.BuildTriggers != null )
            {
                console.WriteError(
                    $"{description} is replaced by a configuration that sets BuildTriggers, which the generator overrides. "
                    + "Declare them in BuildConfigurationInfo.BuildTriggers instead." );

                isValid = false;
            }

            // The declared flag is not the condition. The default public configuration sets it, so testing the flag
            // alone would reject every product that uses that record unchanged.
            if ( TeamCitySettingsFile.RequiresUpstreamCheck( product, configurationInfo ) )
            {
                console.WriteError(
                    $"{description} is replaced by a configuration, so it generates no upstream check, but the product requires "
                    + "one. Clear BuildConfigurationInfo.RequiresUpstreamCheck, or keep the standard build configuration." );

                isValid = false;
            }

            return isValid;
        }

        bool TryAddNode( AdditionalCiBuildConfiguration configuration, string objectName, string description )
        {
            var isValid = true;
            var targets = new List<string>();

            edges[objectName] = targets;

            ImmutableArray<SnapshotDependency> dependencies;

            try
            {
                dependencies = configuration.GetSnapshotDependencies();
            }
            catch ( InvalidOperationException exception )
            {
                console.WriteError( exception.Message );

                return false;
            }

            foreach ( var dependency in dependencies )
            {
                if ( dependency.ConfigurationId != null && !configurationsById.ContainsKey( dependency.ConfigurationId ) )
                {
                    console.WriteError(
                        $"{description} depends on '{dependency.ConfigurationId}', but the product declares no additional build "
                        + $"configuration of that identifier. Declared identifiers: "
                        + $"{string.Join( ", ", configurationsById.Keys.OrderBy( i => i, StringComparer.Ordinal ) )}." );

                    isValid = false;

                    continue;
                }

                if ( dependency.Configuration != null && !product.Configurations[dependency.Configuration.Value].ExportsToTeamCityBuild )
                {
                    console.WriteError(
                        $"{description} depends on the '{dependency.Configuration.Value}' build configuration, which is not exported "
                        + "to TeamCity and therefore has no build type to depend on." );

                    isValid = false;

                    continue;
                }

                // A dependency that downloads nothing is an ordering constraint, which is the snapshot dependency
                // itself. Reusing the last successful build suppresses that snapshot, so the two together emit
                // neither block and the dependency disappears from the generated configuration without a word.
                if ( dependency.ArtifactRules is { Length: 0 }
                     && ( dependency.ReuseLastSuccessfulBuild ?? configuration.EffectiveReuseLastSuccessfulBuild ) )
                {
                    console.WriteError(
                        $"{description} depends on '{dependency}' without artifact rules, which makes the dependency an ordering "
                        + "constraint, and also reuses the last successful build, which removes the ordering. The dependency would "
                        + "generate nothing. Give it artifact rules, or clear ReuseLastSuccessfulBuild." );

                    isValid = false;

                    continue;
                }

                var targetObjectName = dependency.TryGetObjectName( product, configurations )!;

                if ( string.Equals( targetObjectName, objectName, StringComparison.Ordinal ) )
                {
                    console.WriteError( $"{description} depends on itself." );

                    isValid = false;

                    continue;
                }

                targets.Add( targetObjectName );
            }

            isValid &= TryValidateArtifactsLayout( configuration, dependencies, description );

            return isValid;
        }

        bool TryValidateArtifactsLayout(
            AdditionalCiBuildConfiguration configuration,
            ImmutableArray<SnapshotDependency> dependencies,
            string description )
        {
            // BuildSnapshotDependency answers two questions: which build configuration to depend on, and whose artifact
            // layout the checkout is prepared for. Where the dependencies are declared separately, only the second job
            // remains, and nothing else ties the two together. A layout read from a configuration this build does not
            // wait for fails late and unreadably: CopyNuGetConfig and CreateVersionsFile address a directory that the
            // build never downloaded, so it fails on a missing file with nothing to say why.
            var targetedConfigurations = dependencies
                .Where( d => d.Configuration != null )
                .Select( d => d.Configuration!.Value )
                .Distinct()
                .ToList();

            if ( targetedConfigurations.Count == 0 )
            {
                // Every dependency is on an additional configuration, so the layout falls back to the public one.
                // That is the documented behaviour and is what a product with intermediate configurations relies on.
                return true;
            }

            if ( configuration.BuildSnapshotDependency == null )
            {
                console.WriteError(
                    $"{description} depends on the {string.Join( " and ", targetedConfigurations.Select( c => $"'{c}'" ) )} build "
                    + "configuration, but does not set BuildSnapshotDependency, so the artifact layout falls back to 'Public'. Set "
                    + "BuildSnapshotDependency to the configuration whose artifacts the checkout is prepared for." );

                return false;
            }

            if ( targetedConfigurations.Any( c => c != configuration.BuildSnapshotDependency.Value ) )
            {
                console.WriteError(
                    $"{description} reads the artifact layout of the '{configuration.BuildSnapshotDependency.Value}' build "
                    + $"configuration but depends on {string.Join( " and ", targetedConfigurations.Select( c => $"'{c}'" ) )}. The "
                    + "layout would be read from a configuration this build does not wait for." );

                return false;
            }

            return true;
        }

        bool TryValidateCycles()
        {
            // Iterative rather than recursive: a product with dozens of configurations that aggregate one another can
            // nest deeply, and a stack overflow inside a validator is a worse failure than the one it looks for.
            var states = new Dictionary<string, int>( StringComparer.Ordinal );
            var parents = new Dictionary<string, string>( StringComparer.Ordinal );
            var isValid = true;

            foreach ( var root in edges.Keys )
            {
                if ( states.GetValueOrDefault( root ) != 0 )
                {
                    continue;
                }

                var stack = new Stack<(string Node, bool IsExit)>();
                stack.Push( (root, false) );

                while ( stack.Count > 0 )
                {
                    var (node, isExit) = stack.Pop();

                    if ( isExit )
                    {
                        states[node] = 2;

                        continue;
                    }

                    if ( states.GetValueOrDefault( node ) != 0 )
                    {
                        continue;
                    }

                    states[node] = 1;
                    stack.Push( (node, true) );

                    foreach ( var target in edges.GetValueOrDefault( node ) ?? [] )
                    {
                        switch ( states.GetValueOrDefault( target ) )
                        {
                            case 1:
                                console.WriteError(
                                    $"The build configurations of '{product.ProductName}' form a cycle of dependencies: "
                                    + $"{DescribeCycle( node, target )}. TeamCity cannot schedule such a chain." );

                                isValid = false;

                                break;

                            case 0:
                                parents[target] = node;
                                stack.Push( (target, false) );

                                break;
                        }
                    }
                }
            }

            return isValid;

            string DescribeCycle( string from, string to )
            {
                var path = new List<string>();
                var current = from;

                while ( true )
                {
                    path.Add( current );

                    if ( string.Equals( current, to, StringComparison.Ordinal ) || !parents.TryGetValue( current, out var parent ) )
                    {
                        break;
                    }

                    current = parent;
                }

                path.Reverse();
                path.Add( to );

                return string.Join( " -> ", path );
            }
        }
    }
}
