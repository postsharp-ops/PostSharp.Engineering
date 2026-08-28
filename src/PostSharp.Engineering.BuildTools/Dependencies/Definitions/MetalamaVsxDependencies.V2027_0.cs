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
            UpstreamProductFamily = V2026_1.Family,
            GitHubAppConnectionId = GitHubAppConnections.Metalama

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
                MetalamaDependencies.V2027_0.Metalama
                    .ToDependency( _metalamaConfigurations )
                    .WithLastSuccessfulOnly(),
                // Metalama 2026.1 is still under development, so its public builds are on develop/2026.1 and can be
                // resolved there. Once 2026.1 is released and TeamCity has cleaned up the artifacts of that branch,
                // add WithPublishingBranch() here, as the 2026.1 family does for Metalama 2026.0.
                MetalamaDependencies.V2026_1.Metalama
                    .ToDependency( _metalamaConfigurations )
                    .WithAlias( "Metalama20261" )
                    .WithLastSuccessfulOnly(),
                // PostSharp 2026.0, not 2027.0. PostSharp 2027.0 has no branch and no build configuration yet, so a
                // reference to it would resolve to a build that does not exist and the dependency could not be fetched.
                // Move this to PostSharpDependencies.V2027_0 once that family produces a signed distribution.
                PostSharpDependencies.V2026_0.PostSharp.ToDependency(
                        new ConfigurationSpecific<BuildConfiguration>(
                            BuildConfiguration.Release,
                            BuildConfiguration.Release,
                            BuildConfiguration.Release ) )
                    .WithLastSuccessfulOnly()
            ]
        };
    }
}
