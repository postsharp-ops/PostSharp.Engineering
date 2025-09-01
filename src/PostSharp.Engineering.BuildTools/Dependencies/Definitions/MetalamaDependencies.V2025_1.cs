// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Docker;
using System;
using System.IO;
using System.Runtime.InteropServices.Marshalling;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

public static partial class MetalamaDependencies
{
    // ReSharper disable once InconsistentNaming

    [PublicAPI]
    public static class V2025_1
    {
        private class MetalamaDependencyDefinition : DependencyDefinition
        {
            public MetalamaDependencyDefinition(
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
                    CreateMetalamaVcsRepository(
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
                    isVersioned ) { }
        }

        public static ProductFamily Family { get; } = new( _projectName, "2025.1", DevelopmentDependencies.Family, PostSharpDependencies.V2025_1_GitHub.Family )
        {
            DockerBaseImage = DockerImages.WindowsServerCore, UpstreamProductFamily = V2025_0.Family

            // DownstreamProductFamily = V2025_2.Family
        };

        public static DependencyDefinition Consolidated { get; } =
            new MetalamaDependencyDefinition(
                ProductFamily.ConsolidatedProjectName,
                VcsProvider.AzureDevOps,
                null,
                false,
                customRepositoryName: "Metalama.Consolidated" );

        // The release build is intentionally used for the debug configuration because we want dependencies to consume the release
        // build, for performance reasons. The debug build will be used only locally, and for this we don't need a configuration here.
        public static DependencyDefinition MetalamaCompiler { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Compiler",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama )
            {
                EngineeringDirectory = "eng-Metalama",
                PrivateArtifactsDirectory = Path.Combine( "artifacts", "packages", "$(MSSBuildConfiguration)", "Shipping" )
            };

        public static DependencyDefinition Metalama { get; } =
            new MetalamaDependencyDefinition(
                "Metalama",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama )
            {
                // SuppressUpstream = true
            };

        public static DependencyDefinition MetalamaPremium { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Premium",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama )
            {
                // SuppressUpstream = true
            };

        public static DependencyDefinition MetalamaVsx { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Vsx",
                VcsProvider.AzureDevOps,
                null );

        public static DependencyDefinition MetalamaSamples { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Samples",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama ) { CodeStyle = "Metalama.Samples" };

        public static DependencyDefinition TimelessDotNetEngineer { get; } =
            new MetalamaDependencyDefinition(
                "TimelessDotNetEngineer",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.PostSharp ) { CodeStyle = "Metalama.Samples" };

        public static DependencyDefinition MetalamaCommunity { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Community",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.PostSharp );

        public static DependencyDefinition MetalamaDocumentation { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Documentation",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama,
                false );

        public static DependencyDefinition NopCommerce { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Tests.NopCommerce",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.PostSharp,
                false,
                parentCiProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}_MetalamaTests",
                vcsRootProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}",
                customBranch: $"dev/{Family.Version}" );

        public static DependencyDefinition CargoSupport { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Tests.CargoSupport",
                VcsProvider.AzureDevOps,
                null,
                false,
                parentCiProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}_MetalamaTests",
                vcsRootProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}" );

        public static DependencyDefinition DotNetSdkTests { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Tests.DotNetSdk",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.PostSharp,
                false,
                parentCiProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}_MetalamaTests",
                vcsRootProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}" );

        public static DependencyDefinition MetalamaPerformance { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Performance",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.PostSharp,
                false );
    }
}