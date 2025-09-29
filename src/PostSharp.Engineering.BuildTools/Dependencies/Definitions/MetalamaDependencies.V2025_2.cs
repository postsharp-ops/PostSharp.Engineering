// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using System;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

public static partial class MetalamaDependencies
{
    // ReSharper disable once InconsistentNaming

    [PublicAPI]
    public static class V2025_2
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

        public static ProductFamily Family { get; } = new( _projectName, "2025.2", DevelopmentDependencies.Family, PostSharpDependencies.V2025_1.Family )
        {
            UpstreamProductFamily = V2025_1.Family, DownstreamProductFamily = V2026_0.Family
        };

        // The release build is intentionally used for the debug configuration because we want dependencies to consume the release
        // build, for performance reasons. The debug build will be used only locally, and for this we don't need a configuration here.
        public static DependencyDefinition MetalamaCompiler { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Compiler",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama )
            {
                EngineeringDirectory = "eng-Metalama",
#pragma warning disable CS0618 // Type or member is obsolete
                ParametricPrivateArtifactsDirectory = Path.Combine( "artifacts", "packages", "$(MSBuildConfiguration)", "Shipping" ),
#pragma warning restore CS0618 // Type or member is obsolete
                Dependencies = [DevelopmentDependencies.PostSharpEngineering]
            };

        public static DependencyDefinition Metalama { get; } =
            new MetalamaDependencyDefinition(
                "Metalama",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama )
            {
                PackagePatterns =
                [
                    "Metalama.Backstage*",
                    "Metalama.Framework*",
                    "Metalama.Extensions.DependencyInjection",
                    "Metalama.Extensions.DependencyInjection.ServiceLocator",
                    "Metalama.Extensions.Metrics",
                    "Metalama.Extensions.Multicast",
                    "Metalama.Patterns.Caching",
                    "Metalama.Patterns.Caching.Aspects",
                    "Metalama.Patterns.Caching.Backend",
                    "Metalama.Patterns.Caching.TestHelpers",
                    "Metalama.Patterns.Contracts",
                    "Metalama.Patterns.Immutability",
                    "Metalama.Patterns.Memoization",
                    "Metalama.Patterns.Observability",
                    "Metalama.Patterns.TestHelpers",
                    "Metalama.Patterns.Wpf",
                    "Metalama.LinqPad",
                    "Metalama.Migration",
                    "Metalama.Testing.*",
                    "Metalama.Tool",
                    "Flashtrace*"
                ],
                Dependencies =
                [
                    DevelopmentDependencies.PostSharpEngineering,
                    MetalamaCompiler.ToDependency(
                        new ConfigurationSpecific<BuildConfiguration>( BuildConfiguration.Release, BuildConfiguration.Release, BuildConfiguration.Public ) )
                ]
            };

        public static DependencyDefinition MetalamaPremium { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Premium",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama )
            {
                PackagePatterns =
                [
                    "Metalama.Extensions.Architecture",
                    "Metalama.Extensions.CodeFixes",
                    "Metalama.Extensions.CodeFixes.Redist",
                    "Metalama.Extensions.Validation",
                    "Metalama.Extensions.Validation.Redist",
                    "Metalama.Patterns.Caching.Backends.Azure",
                    "Metalama.Patterns.Caching.Backends.Redis",
                    "Metalama.Licensing"
                ],
                Dependencies = [DevelopmentDependencies.PostSharpEngineering, Metalama]
            };

        public static DependencyDefinition MetalamaVsx { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Vsx",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama )
            {
                PackagePatterns = ["Metalama.Repacked"],
                Dependencies =
                [
                    DevelopmentDependencies.PostSharpEngineering, Metalama,
                    PostSharpDependencies.V2025_1.PostSharp.ToDependency(
                        new ConfigurationSpecific<BuildConfiguration>(
                            BuildConfiguration.Release,
                            BuildConfiguration.Release,
                            BuildConfiguration.Release ) )
                ]
            };

        public static DependencyDefinition MetalamaSamples { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Samples",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama )
            {
                CodeStyle = "Metalama.Samples",
                PackagePatterns = ["Metalama.Documentation.QuickStart"],
                Dependencies = [DevelopmentDependencies.PostSharpEngineering, MetalamaPremium]
            };

        public static DependencyDefinition TimelessDotNetEngineer { get; } =
            new MetalamaDependencyDefinition(
                "TimelessDotNetEngineer",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.PostSharp ) { CodeStyle = "Metalama.Samples" };

        public static DependencyDefinition MetalamaCommunity { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Community",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama ) { Dependencies = [DevelopmentDependencies.PostSharpEngineering, Metalama] };

        public static DependencyDefinition MetalamaDocumentation { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Documentation",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama,
                false )
            {
                Dependencies = [DevelopmentDependencies.PostSharpEngineering, MetalamaSamples], SourceDependencies = [MetalamaSamples, MetalamaCommunity]
            };

        public static DependencyDefinition NopCommerce { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Tests.NopCommerce",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.PostSharp,
                false,
                parentCiProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}_MetalamaTests",
                vcsRootProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}",
                customBranch: $"dev/{Family.Version}" ) { Dependencies = [DevelopmentDependencies.PostSharpEngineering, Metalama] };

        public static DependencyDefinition DotNetSdkTests { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Tests.DotNetSdk",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama,
                false,
                parentCiProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}_MetalamaTests",
                vcsRootProjectId: $"Metalama_Metalama{Family.VersionWithoutDots}" )
            {
                Dependencies = [DevelopmentDependencies.PostSharpEngineering, Metalama]
            };

        public static DependencyDefinition MetalamaPerformance { get; } =
            new MetalamaDependencyDefinition(
                "Metalama.Performance",
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama,
                false ) { Dependencies = [DevelopmentDependencies.PostSharpEngineering, Metalama] };

        public static DependencyDefinition Consolidated { get; } =
            new MetalamaDependencyDefinition(
                ProductFamily.ConsolidatedProjectName,
                VcsProvider.GitHub,
                MetalamaGitHubOrganization.Metalama,
                false,
                customRepositoryName: "Metalama.Consolidated" )
            {
                Dependencies =
                [
                    DevelopmentDependencies.PostSharpEngineering,
                    MetalamaCompiler.ToDependency(
                        new ConfigurationSpecific<BuildConfiguration>( BuildConfiguration.Release, BuildConfiguration.Release, BuildConfiguration.Public ) ),
                    Metalama,
                    MetalamaCommunity,
                    MetalamaPremium,
                    MetalamaSamples,
                    MetalamaDocumentation,
                    MetalamaVsx
                ],
                SourceDependencies =
                [
                    MetalamaCompiler,
                    Metalama,
                    MetalamaCommunity,
                    MetalamaPremium,
                    MetalamaSamples,
                    MetalamaDocumentation,
                    MetalamaVsx,
                    NopCommerce
                ]
            };
    }
}