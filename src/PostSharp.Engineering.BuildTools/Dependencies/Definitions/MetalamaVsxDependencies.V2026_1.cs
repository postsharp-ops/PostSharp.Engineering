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
    public static class V2026_1
    {
        public static ProductFamily Family { get; } = new( _projectName, "2026.1", DevelopmentDependencies.Family, MetalamaDependencies.V2026_1.Family )
        {
            GitHubAppConnectionId = GitHubAppConnections.Metalama

            // No UpstreamProductFamily - before 2026.1, Metalama.Vsx was a product of the Metalama family.
            // DownstreamProductFamily = V2026_2.Family
        };

        /// <summary>
        /// Metalama.Vsx is the only product of its family, so the family has no per-product project level in TeamCity:
        /// the version-level project directly holds the build configurations, and its VCS root - which has the same
        /// identifier - is stored in the <c>MetalamaVsx</c> project above it.
        /// </summary>
        private static readonly TeamCityProjectId _teamCityProjectId =
            TeamCityHelper.GetSingleProductFamilyProjectId( _projectName, Family.Version );

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
                MetalamaDependencies.V2026_1.Metalama
                    .ToDependency()
                    .WithLastSuccessfulOnly(),
                MetalamaDependencies.V2026_0.Metalama
                    .ToDependency(
                        new ConfigurationSpecific<BuildConfiguration>(
                            BuildConfiguration.Public,
                            BuildConfiguration.Public,
                            BuildConfiguration.Public ) )
                    .WithAlias( "Metalama20260" )
                    .WithLastSuccessfulOnly(),
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
