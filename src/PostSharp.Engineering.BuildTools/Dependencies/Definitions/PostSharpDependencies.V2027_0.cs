// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

public static partial class PostSharpDependencies
{
    // ReSharper disable once InconsistentNaming

    [PublicAPI]
    public static class V2027_0
    {
        public static ProductFamily Family { get; } =
            new( _projectName, "2027.0", DevelopmentDependencies.Family, BackstageDependencies.V2027_0.Family )
            {
                GitHubAppConnectionId = GitHubAppConnections.PostSharp,
                UpstreamProductFamily = V2026_0.Family,

                // The repositories of the line bump their version and deploy together, and they publish from the
                // release branch instead of the development branch.
                ConsolidatedProjectName = "PostSharp.Consolidated"
            };

        /// <summary>
        /// The TeamCity project of this line. It carries no build configuration of its own: it contains one project
        /// per repository of the line, the arrangement the Metalama lines and the 2026.0 line use. The 2024.0 line is
        /// flat instead -- its line project is the project of the PostSharp repository.
        /// </summary>
        private static readonly string _lineProjectId =
            TeamCityHelper.GetProjectIdWithParentProjectId( $"{_projectName} {Family.Version}", _parentProjectId ).Id;

        private static TeamCityProjectId GetProjectId( string dependencyName )
            => TeamCityHelper.GetProjectIdWithParentProjectId( dependencyName, _lineProjectId );

        /// <summary>
        /// The identifier of the VCS root of a repository of this line. The roots are stored in the PostSharp project
        /// and named after the repository and the version, as those of the previous lines are, rather than in the line
        /// project as the per-repository projects would imply. That is where they exist on TeamCity, and the generated
        /// settings address them by identifier, so the identifier has to be the one TeamCity carries.
        /// </summary>
        private static string GetVcsRootId( string dependencyName )
            => TeamCityHelper.GetProjectIdWithParentProjectId( $"{dependencyName} {Family.Version}", _parentProjectId ).Id;

        /// <summary>
        /// A repository of this line: it builds from the development branch, publishes from the release branch, and
        /// owns a TeamCity project named after itself beneath the project of the line.
        /// </summary>
        private class PostSharpDependencyDefinition : DependencyDefinition
        {
            public PostSharpDependencyDefinition( string dependencyName, bool isVersioned = true )
                : base(
                    Family,
                    dependencyName,
                    $"develop/{Family.Version}",
                    $"release/{Family.Version}",
                    new GitHubRepository( dependencyName, _projectName ),
                    TeamCityHelper.CreateConfiguration(
                        GetProjectId( dependencyName ),
                        isVersioned,
                        vcsRootProjectId: _parentProjectId,
                        vcsRootId: GetVcsRootId( dependencyName ) ),
                    isVersioned ) { }
        }

        /// <summary>The compiler and the pattern libraries.</summary>
        public static DependencyDefinition PostSharp { get; } = new PostSharpDependencyDefinition( _projectName )
        {
            // The line is consolidated, so its builds are chained: the consolidated build and the other repositories of
            // the line take a TeamCity snapshot dependency on this build and restore its packages from its artifacts
            // instead of from the package feed. Setting GenerateSnapshotDependency to false would leave the consolidated
            // build unchained from the product it consolidates.
            //
            // Backstage is the shared licensing, telemetry and configuration library that the compiler is built
            // on since 2027.0. It belongs to another family, which is why the family of this line declares it as a
            // related family; listing it here is what gives the PostSharp build a TeamCity snapshot dependency on
            // the Backstage build and what puts the Backstage artifacts among the package sources of the generated
            // nuget.config, so that the compiler restores the packages of the build it was chained to rather than
            // from a feed they are not published to. Each build configuration of PostSharp takes the same configuration of
            // Backstage: the Release pipeline and the Public pipeline of PostSharp are tested separately.
            Dependencies =
            [
                DevelopmentDependencies.PostSharpEngineering,
                BackstageDependencies.V2027_0.Backstage.ToDependency()
            ],
            // Since 2027.0, PostSharp.Sdk holds the reference assemblies that add-ins compile against, and the
            // PostSharp.Compiler.* packages only depend on it. PostSharp.Build.Tools is a private package: the MSBuild SDK
            // of the build tasks and the test tools, which the tests import from the artifacts of the build.
            PackagePatterns =
            [
                "PostSharp",
                "PostSharp.Redist",
                "PostSharp.Sdk",
                "PostSharp.Build.Tools",
                "PostSharp.Compiler.*",
                "PostSharp.Patterns.*",
                "PostSharp.Settings.*"
            ],
            AutoUpdateVersion = false
        };

        /// <summary>The documentation site, which documents this line and is built against its packages.</summary>
        public static DependencyDefinition PostSharpDocumentation { get; } =
            new PostSharpDependencyDefinition( $"{_projectName}.Documentation", isVersioned: false )
            {
                Dependencies =
                [
                    DevelopmentDependencies.PostSharpEngineering.ToDependency(),

                    // The debug and release builds of the documentation take the release build of PostSharp, which
                    // runs on the development branch. This line publishes from the release branch, which is updated
                    // only when the line is deployed, so the public build left on the development branch is older
                    // than the current packages. This line generates no debug build of PostSharp.
                    PostSharp.ToDependency(
                        new ConfigurationSpecific<BuildConfiguration>(
                            BuildConfiguration.Release,
                            BuildConfiguration.Release,
                            BuildConfiguration.Public ) )
                ]
            };

        /// <summary>
        /// The .NET SDK and platform compatibility harness: it generates a throwaway application from a stock
        /// `dotnet new` template, builds it against this line's packages, and asserts the weaver ran. It ships
        /// nothing, so it is not versioned, and it runs on GitHub Actions rather than TeamCity -- the matrix of
        /// operating systems and SDK installation sources is the point, and that is what GitHub's hosted runners
        /// provide. It still needs a CI configuration here, because that is where the name of the TeamCity token
        /// and the address of the server are read from when it downloads the packages it tests.
        /// </summary>
        public static DependencyDefinition DotNetSdkTests { get; } =
            new PostSharpDependencyDefinition( $"{_projectName}.Tests.DotNetSdk", isVersioned: false )
            {
                Dependencies =
                [
                    DevelopmentDependencies.PostSharpEngineering.ToDependency(),

                    // The harness tests the release build of PostSharp, not the public one. This line publishes
                    // from the release branch, which is updated only when the line is deployed, so a harness pinned to
                    // the public build would keep testing the last deployed build. The release build runs on the
                    // development branch. The dependency is pinned in every configuration, because an unpinned
                    // dependency would resolve the debug build type, which this line does not generate.
                    PostSharp.ToDependency(
                        new ConfigurationSpecific<BuildConfiguration>(
                            BuildConfiguration.Release,
                            BuildConfiguration.Release,
                            BuildConfiguration.Release ) )
                ]
            };

        /// <summary>
        /// The consolidated product of the line. It builds no code of its own: it chains the builds of the
        /// repositories it lists, bumps their version in one operation, and deploys them together. Backstage belongs
        /// to another family, but this line is built and deployed against it, so it takes part in this build, as it
        /// does in the consolidated build of the Metalama line.
        /// </summary>
        public static DependencyDefinition Consolidated { get; } =
            new PostSharpDependencyDefinition( $"{_projectName}.Consolidated", isVersioned: false )
            {
                IsConsolidated = true,
                Dependencies =
                [
                    DevelopmentDependencies.PostSharpEngineering.ToDependency(),
                    BackstageDependencies.V2027_0.Backstage.ToDependency(),

                    // As for the documentation and the SDK tests, PostSharp exports only its public build.
                    PostSharp.ToDependency(
                        new ConfigurationSpecific<BuildConfiguration>(
                            BuildConfiguration.Public,
                            BuildConfiguration.Public,
                            BuildConfiguration.Public ) ),
                    PostSharpDocumentation.ToDependency()
                ],
                SourceDependencies = [BackstageDependencies.V2027_0.Backstage, PostSharp, PostSharpDocumentation]
            };
    }
}
