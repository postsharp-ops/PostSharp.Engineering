// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

public static partial class MetalamaVsxDependencies
{
    // ReSharper disable once InconsistentNaming

    [PublicAPI]
    public static class V2027_0
    {
        public static ProductFamily Family { get; } = new( _projectName, "2027.0", DevelopmentDependencies.Family, MetalamaDependencies.V2027_0.Family )
        {
            UpstreamProductFamily = V2026_1.Family, GitHubAppConnectionId = GitHubAppConnections.Metalama

            // DownstreamProductFamily = V2027_1.Family
        };

        /// <summary>
        /// Metalama.Vsx is the only product of its family, so the family has no per-product project level in TeamCity:
        /// the version-level project directly holds the build configurations, and its VCS root - which has the same
        /// identifier - is stored in the <c>MetalamaVsx</c> project above it.
        /// </summary>
        private static readonly TeamCityProjectId _teamCityProjectId =
            TeamCityHelper.GetSingleProductFamilyProjectId( _projectName, Family.Version );

        /// <summary>
        /// The configuration mapping shared by both Metalama dependencies: the last good build of Metalama in the
        /// configuration matching the one of Metalama.Vsx.
        /// </summary>
        private static readonly ConfigurationSpecific<BuildConfiguration> _metalamaConfigurations =
            new( BuildConfiguration.Debug, BuildConfiguration.Release, BuildConfiguration.Public );

        public static DependencyDefinition MetalamaVsx { get; } = new(
            Family,
            _projectName,
            $"develop/{Family.Version}",
            $"release/{Family.Version}",
            MetalamaDependencies.CreateMetalamaVcsRepository( _projectName, VcsProvider.GitHub, MetalamaGitHubOrganization.Metalama, null ),
            TeamCityHelper.CreateConfiguration( _teamCityProjectId, vcsRootId: _teamCityProjectId.Id ) )
        {
            PublishesFromReleaseBranch = true,
            PackagePatterns = ["Metalama.Repacked"],
            Dependencies =
            [
                DevelopmentDependencies.PostSharpEngineering,

                // Metalama.Vsx merges the Backstage assemblies into Metalama.Repacked, so it needs the package source mapping
                // and the version of Backstage in addition to those of Metalama.
                BackstageDependencies.V2027_0.Backstage,
                MetalamaDependencies.V2027_0.Metalama
                    .ToDependency( _metalamaConfigurations )
                    .WithLastSuccessfulOnly(),

                // The canonical variant of Metalama.Vsx is built on Metalama 2027.0, which supports the projects that use
                // Metalama 2026.1 or later. The 2026.0 variant still needs Metalama 2026.0. Metalama 2026.0 is released:
                // it publishes only from release/2026.0, and TeamCity has cleaned up the artifacts of the last public
                // build on develop/2026.0. Look its builds up on the publishing branch instead.
                MetalamaDependencies.V2026_0.Metalama
                    .ToDependency( _metalamaConfigurations )
                    .WithAlias( "Metalama20260" )
                    .WithPublishingBranch()
                    .WithLastSuccessfulOnly(),

                // PostSharp exports only its public build -- the signed distribution -- so that is the configuration
                // to resolve, whichever configuration this product is itself built in.
                PostSharpDependencies.V2027_0.PostSharp.ToDependency(
                        new ConfigurationSpecific<BuildConfiguration>(
                            BuildConfiguration.Public,
                            BuildConfiguration.Public,
                            BuildConfiguration.Public ) )
                    .WithLastSuccessfulOnly()
            ]
        };
    }
}