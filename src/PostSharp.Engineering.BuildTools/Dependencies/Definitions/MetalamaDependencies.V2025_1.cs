// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Docker;
using System;
using System.IO;

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

        public static ProductFamily Family { get; } = new( _projectName, "2025.1", DevelopmentDependencies.Family, PostSharpDependencies.V2025_0.Family )
        {
            DockerBaseImage = DockerImages.WindowsServerCore, UpstreamProductFamily = V2025_0.Family

            // DownstreamProductFamily = V2025_2.Family
        };

        public static DependencyDefinition MetalamaBackstage { get; } = new MetalamaDependencyDefinition( "Metalama.Backstage", VcsProvider.GitHub );

        public static DependencyDefinition Consolidated { get; } = new MetalamaDependencyDefinition(
            ProductFamily.ConsolidatedProjectName,
            VcsProvider.AzureDevOps,
            false,
            customRepositoryName: "Metalama.Consolidated" );

        // The release build is intentionally used for the debug configuration because we want dependencies to consume the release
        // build, for performance reasons. The debug build will be used only locally, and for this we don't need a configuration here.
        public static DependencyDefinition MetalamaCompiler { get; } = new MetalamaDependencyDefinition(
            "Metalama.Compiler",
            VcsProvider.GitHub )
        {
            EngineeringDirectory = "eng-Metalama", PrivateArtifactsDirectory = Path.Combine( "artifacts", "packages", "$(MSSBuildConfiguration)", "Shipping" )
        };

        public static DependencyDefinition Metalama { get; } = new MetalamaDependencyDefinition(
            "Metalama",
            VcsProvider.GitHub,
            customRepositoryName: "Metalama.Framework" );

        public static DependencyDefinition MetalamaVsx { get; } = new MetalamaDependencyDefinition( "Metalama.Vsx", VcsProvider.AzureDevOps );

        public static DependencyDefinition MetalamaExtensions { get; } = new MetalamaDependencyDefinition( "Metalama.Extensions", VcsProvider.GitHub );

        public static DependencyDefinition MetalamaSamples { get; } =
            new MetalamaDependencyDefinition( "Metalama.Samples", VcsProvider.GitHub ) { CodeStyle = "Metalama.Samples" };

        public static DependencyDefinition TimelessDotNetEngineer { get; } =
            new MetalamaDependencyDefinition( "TimelessDotNetEngineer", VcsProvider.GitHub ) { CodeStyle = "Metalama.Samples" };

        public static DependencyDefinition MetalamaMigration { get; } = new MetalamaDependencyDefinition( "Metalama.Migration", VcsProvider.GitHub );

        public static DependencyDefinition MetalamaLinqPad { get; } = new MetalamaDependencyDefinition( "Metalama.LinqPad", VcsProvider.GitHub );

        public static DependencyDefinition MetalamaCommunity { get; } = new MetalamaDependencyDefinition( "Metalama.Community", VcsProvider.GitHub );

        public static DependencyDefinition MetalamaDocumentation { get; } = new MetalamaDependencyDefinition(
            "Metalama.Documentation",
            VcsProvider.GitHub,
            false );

        public static DependencyDefinition MetalamaTry { get; } =
            new MetalamaDependencyDefinition( "Metalama.Try", VcsProvider.AzureDevOps, false ) { EngineeringDirectory = "eng-Metalama" };

        public static DependencyDefinition PostSharpPatterns { get; } = new MetalamaDependencyDefinition(
            "PostSharp.Patterns",
            VcsProvider.AzureDevOps,
            false );

        public static DependencyDefinition MetalamaPatterns { get; } = new MetalamaDependencyDefinition(
            "Metalama.Patterns",
            VcsProvider.GitHub );

        public static DependencyDefinition NopCommerce { get; } = new MetalamaDependencyDefinition(
            "Metalama.Tests.NopCommerce",
            VcsProvider.GitHub,
            false,
            parentCiProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}_MetalamaTests",
            vcsRootProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}",
            customBranch: $"dev/{Family.Version}" );

        public static DependencyDefinition CargoSupport { get; } = new MetalamaDependencyDefinition(
            "Metalama.Tests.CargoSupport",
            VcsProvider.AzureDevOps,
            false,
            parentCiProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}_MetalamaTests",
            vcsRootProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}" );
        
        public static DependencyDefinition DotNetSdkTests { get; } = new MetalamaDependencyDefinition(
            "Metalama.Tests.DotNetSdk",
            VcsProvider.GitHub,
            false,
            parentCiProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}_MetalamaTests",
            vcsRootProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}" );

        public static DependencyDefinition MetalamaPerformance { get; } = new MetalamaDependencyDefinition(
            "Metalama.Performance",
            VcsProvider.GitHub,
            false );
    }
}