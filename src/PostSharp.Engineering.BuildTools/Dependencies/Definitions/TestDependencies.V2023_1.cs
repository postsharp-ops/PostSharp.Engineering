// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

public static partial class TestDependencies
{
    // ReSharper disable once InconsistentNaming

    [PublicAPI]
    public static class V2023_1
    {
        private class TestDependencyDefinition : DependencyDefinition
        {
            public TestDependencyDefinition(
                string dependencyName,
                VcsProvider vcsProvider,
                bool isVersioned = true,
                string? customRepositoryName = null )
                : base(
                    Family,
                    dependencyName,
                    $"develop/{Family.Version}",
                    $"release/{Family.Version}",
                    CreateEngineeringVcsRepository( customRepositoryName ?? dependencyName, vcsProvider ),
                    TeamCityHelper.CreateConfiguration(
                        TeamCityHelper.GetProjectIdWithParentProjectId(
                            dependencyName,
                            $"Test_Test{Family.VersionWithoutDots}" ),
                        isVersioned ),
                    isVersioned ) { }
        }

        public static ProductFamily Family { get; } =
            new( _projectName, "2023.1", DevelopmentDependencies.Family )
            {
                ConsolidatedProjectName = "Consolidated", GitHubAppConnectionId = GitHubAppConnections.PostSharpOps
            };

        public static DependencyDefinition Consolidated { get; } = new TestDependencyDefinition(
            "Consolidated",
            VcsProvider.AzureDevOps,
            false,
            customRepositoryName: "PostSharp.Engineering.Test.Consolidated" );

        public static DependencyDefinition TestProduct { get; } = new TestDependencyDefinition(
            "PostSharp.Engineering.Test.TestProduct",
            VcsProvider.AzureDevOps );

        public static DependencyDefinition Dependency { get; } = new TestDependencyDefinition(
            "PostSharp.Engineering.Test.Dependency",
            VcsProvider.AzureDevOps );

        public static DependencyDefinition TransitiveDependency { get; } = new TestDependencyDefinition(
            "PostSharp.Engineering.Test.TransitiveDependency",
            VcsProvider.AzureDevOps );

        public static DependencyDefinition GitHub { get; } = new TestDependencyDefinition(
            "PostSharp.Engineering.Test.GitHub",
            VcsProvider.GitHub );

        public static DependencyDefinition MainVersionDependency { get; } = new TestDependencyDefinition(
            "PostSharp.Engineering.Test.MainVersionDependency",
            VcsProvider.AzureDevOps );

        public static DependencyDefinition PatchVersion { get; } = new TestDependencyDefinition(
            "PostSharp.Engineering.Test.PatchVersion",
            VcsProvider.AzureDevOps );
    }
}