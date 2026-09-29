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

    /// <summary>
    /// The PostSharp 2024.0 line. It is the upstream of <see cref="V2026_0"/>: changes flow from it into the newer
    /// line. The documentation is not part of this family -- it is written for 2026.0 onwards.
    /// </summary>
    [PublicAPI]
    public static class V2024_0
    {
        public static ProductFamily Family { get; } =
            new( _projectName, "2024.0", DevelopmentDependencies.Family )
            {
                GitHubAppConnectionId = GitHubAppConnections.PostSharp,

                // The consolidated product bumps the version and deploys the line. It is also what makes the products
                // of the line publish from the release branch instead of the development branch.
                ConsolidatedProjectName = "PostSharp.Consolidated"
            };

        /// <summary>
        /// Gets the identifier of the TeamCity project of a repository of this line. The line has no TeamCity project
        /// of its own: each repository has a project named after itself and the version, beneath the PostSharp project,
        /// and its VCS root has the same identifier.
        /// </summary>
        private static TeamCityProjectId GetProjectId( string dependencyName )
            => TeamCityHelper.GetProjectIdWithParentProjectId( $"{dependencyName} {Family.Version}", _parentProjectId );

        /// <summary>
        /// The compiler and the pattern libraries. The upstream merge resolves the upstream of a product by its
        /// <see cref="Build.Model.Product.ProductName"/>, which is the name of this definition, so the name has to
        /// match the one the downstream line uses -- see <see cref="V2026_0.PostSharp"/>.
        /// </summary>
        public static DependencyDefinition PostSharp { get; } = new(
            Family,
            _projectName,
            $"develop/{Family.Version}",
            $"release/{Family.Version}",
            new GitHubRepository( _projectName, _projectName ),
            TeamCityHelper.CreateConfiguration(
                GetProjectId( _projectName ),
                vcsRootId: GetProjectId( _projectName ).Id ) )
        {
            // The line is consolidated, so its builds are chained: the consolidated build takes a TeamCity snapshot
            // dependency on the build and on the deployment of this product. Setting GenerateSnapshotDependency to
            // false would leave the consolidated deployment unchained from the product it deploys.
            Dependencies = [DevelopmentDependencies.PostSharpEngineering],

            // The packages this repository builds. The default is the product name followed by ".*", which would claim
            // PostSharp.Engineering.*: package source mapping would then look for the engineering packages in the
            // artifact directory, where they are not, and a restore against the generated nuget.config fails NU1101.
            PackagePatterns = ["PostSharp", "PostSharp.Redist", "PostSharp.Compiler.*", "PostSharp.Patterns.*"],
            AutoUpdateVersion = false
        };

        /// <summary>
        /// The consolidated product of the line. It builds no code of its own: it chains the build of PostSharp, bumps
        /// its version and deploys it. Unlike the 2027.0 line, this line is not built against Backstage, so Backstage is
        /// not part of this build.
        /// </summary>
        public static DependencyDefinition Consolidated { get; } = new(
            Family,
            $"{_projectName}.Consolidated",
            $"develop/{Family.Version}",
            $"release/{Family.Version}",
            new GitHubRepository( $"{_projectName}.Consolidated", _projectName ),
            TeamCityHelper.CreateConfiguration(
                GetProjectId( $"{_projectName}.Consolidated" ),
                false,
                vcsRootId: GetProjectId( $"{_projectName}.Consolidated" ).Id ),
            false )
        {
            IsConsolidated = true,
            Dependencies =
            [
                DevelopmentDependencies.PostSharpEngineering.ToDependency(),

                // PostSharp exports only its public build -- the signed distribution.
                PostSharp.ToDependency(
                    new ConfigurationSpecific<BuildConfiguration>(
                        BuildConfiguration.Public,
                        BuildConfiguration.Public,
                        BuildConfiguration.Public ) )
            ],
            SourceDependencies = [PostSharp]
        };
    }
}
