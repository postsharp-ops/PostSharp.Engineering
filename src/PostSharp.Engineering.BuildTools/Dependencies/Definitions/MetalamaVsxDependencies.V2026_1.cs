// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using System;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

public static partial class MetalamaVsxDependencies
{
    // ReSharper disable once InconsistentNaming

    [PublicAPI]
    public static class V2026_1
    {
        private class MetalamaVsxDependencyDefinition : DependencyDefinition
        {
            public MetalamaVsxDependencyDefinition(
                string dependencyName,
                VcsProvider vcsProvider,
                MetalamaGitHubOrganization? organization,
                bool isVersioned = true,
                string? parentCiProjectId = null,
                string? customCiProjectName = null,
                string? customBranch = null,
                string? customReleaseBranch = null,
                string? customRepositoryName = null,
                bool pullRequestRequiresStatusCheck = true,
                string? vcsRootProjectId = null )
                : base(
                    Family,
                    dependencyName,
                    customBranch ?? $"develop/{Family.Version}",
                    customReleaseBranch ?? $"release/{Family.Version}",
                    MetalamaDependencies.CreateMetalamaVcsRepository(
                        customRepositoryName ?? dependencyName,
                        vcsProvider,
                        organization,
                        customBranch == null && customReleaseBranch == null
                            ? null
                            : $"DefaultBranch_{dependencyName.Replace( ".", "", StringComparison.Ordinal )}" ),
                    TeamCityHelper.CreateConfiguration(
                        parentCiProjectId == null
                            ? TeamCityHelper.GetProjectId( dependencyName, _projectName, Family.Version )
                            : TeamCityHelper.GetProjectIdWithParentProjectId( dependencyName, parentCiProjectId ),
                        isVersioned,
                        pullRequestRequiresStatusCheck: pullRequestRequiresStatusCheck,
                        vcsRootProjectId: vcsRootProjectId ),
                    isVersioned )
            {
                this.PublishesFromReleaseBranch = true;
            }
        }

        public static ProductFamily Family { get; } = new( _projectName, "2026.1", DevelopmentDependencies.Family, MetalamaDependencies.V2026_1.Family )
        {
            UpstreamProductFamily = V2026_0.Family

            // DownstreamProductFamily = V2026_2.Family
        };

        public static DependencyDefinition MetalamaVsx { get; } =
            new MetalamaVsxDependencyDefinition(
                "Metalama.Vsx",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama )
            {
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
