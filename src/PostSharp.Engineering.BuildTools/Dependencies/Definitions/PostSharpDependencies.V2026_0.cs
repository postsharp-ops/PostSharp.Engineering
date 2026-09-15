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
    public static class V2026_0
    {
        public static ProductFamily Family { get; } =
            new( _projectName, "2026.0", DevelopmentDependencies.Family )
            {
                GitHubAppConnectionId = GitHubAppConnections.PostSharp,

                // Changes flow from the 2024.0 line into this one. Declaring the upstream is what generates the
                // 'Upstream Merge' build configuration and the 'Check pending upstream changes' step on the
                // publishing configurations.
                UpstreamProductFamily = V2024_0.Family
            };

        private static TeamCityProjectId GetProjectId( string dependencyName )
            => TeamCityHelper.GetProjectIdWithParentProjectId( $"{dependencyName} {Family.Version}", _parentProjectId );

        /// <summary>
        /// A repository of this line: it builds from the development branch, publishes from the release branch, and
        /// owns a TeamCity project and a VCS root named after itself and the version.
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
                        vcsRootId: GetProjectId( dependencyName ).Id ),
                    isVersioned ) { }
        }

        /// <summary>The compiler and the pattern libraries.</summary>
        public static DependencyDefinition PostSharp { get; } = new PostSharpDependencyDefinition( _projectName )
        {
            GenerateSnapshotDependency = false,
            Dependencies = [DevelopmentDependencies.PostSharpEngineering],

            // The packages this repository builds. The default is the product name followed by ".*", which would claim
            // PostSharp.Engineering.*: package source mapping would then look for the engineering packages in the
            // artifact directory, where they are not, and a restore against the generated nuget.config fails NU1101.
            PackagePatterns = ["PostSharp", "PostSharp.Redist", "PostSharp.Compiler.*", "PostSharp.Patterns.*", "PostSharp.Settings.*"],
            AutoUpdateVersion = false
        };

        /// <summary>The documentation site, which documents this line and is built against its packages.</summary>
        public static DependencyDefinition PostSharpDocumentation { get; } =
            new PostSharpDependencyDefinition( $"{_projectName}.Documentation", isVersioned: false )
            {
                Dependencies =
                [
                    DevelopmentDependencies.PostSharpEngineering.ToDependency(),

                    // PostSharp exports only its public build -- the signed distribution -- so that is what every one
                    // of its consumers resolves, whichever configuration the consumer is itself built in.
                    PostSharp.ToDependency(
                        new ConfigurationSpecific<BuildConfiguration>(
                            BuildConfiguration.Public,
                            BuildConfiguration.Public,
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

                    // As for the documentation, the only configuration PostSharp exports is the public one, and an
                    // unpinned dependency would resolve the debug build type, which is never generated.
                    PostSharp.ToDependency(
                        new ConfigurationSpecific<BuildConfiguration>(
                            BuildConfiguration.Public,
                            BuildConfiguration.Public,
                            BuildConfiguration.Public ) )
                ]
            };
    }
}
