// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharpPackageDependencies = PostSharp.Engineering.BuildTools.Dependencies.Definitions.PostSharpDependencies.V2026_0;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

[PublicAPI]
public static partial class PostSharpDependencies
{
    private const string _projectName = "PostSharp";

    private class PostSharpDocumentationDependencyDefinition : DependencyDefinition
    {
        public PostSharpDocumentationDependencyDefinition(
            string dependencyName,
            VcsProvider vcsProvider )
            : base(
                DocumentationFamily,
                dependencyName,
                "master",
                "master",
                new GitHubRepository( "PostSharp.Documentation", _projectName ),
                new CiProjectConfiguration(
                    new TeamCityProjectId( "PostSharpGitHub_PostSharpDocumentation", "PostSharpGitHub" ),
                    new ConfigurationSpecific<string>(
                        $"PostSharpGitHub_PostSharpDocumentation_DebugBuild",
                        $"PostSharpGitHub_PostSharpDocumentation_ReleaseBuild",
                        $"PostSharpGitHub_PostSharpDocumentation_PublicBuild" ),
                    null,
                    null,
                    EnvironmentVariableNames.TeamCityToken,
                    TeamCityHelper.TeamCityCloudUrl ),
                false ) { }
    }

    public static ProductFamily DocumentationFamily { get; } = new(
        "PostSharp.Documentation",
        "1.0",
        DevelopmentDependencies.Family,
        PostSharpPackageDependencies.Family ) { GitHubAppConnectionId = GitHubAppConnections.PostSharp };

    public static DependencyDefinition PostSharpDocumentation { get; } = new PostSharpDocumentationDependencyDefinition(
        "PostSharp.Documentation",
        VcsProvider.GitHub )
    {
        Dependencies =
        [
            DevelopmentDependencies.PostSharpEngineering.ToDependency(),
            PostSharpPackageDependencies.PostSharp.ToDependency(
                new ConfigurationSpecific<BuildConfiguration>( BuildConfiguration.Release, BuildConfiguration.Release, BuildConfiguration.Release ) )
        ]
    };
}