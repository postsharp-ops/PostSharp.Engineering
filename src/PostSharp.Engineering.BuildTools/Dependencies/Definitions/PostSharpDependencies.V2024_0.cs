// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
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
            new( _projectName, "2024.0", DevelopmentDependencies.Family ) { GitHubAppConnectionId = GitHubAppConnections.PostSharp };

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
                TeamCityHelper.GetProjectIdWithParentProjectId( $"{_projectName} {Family.Version}", _parentProjectId ),
                vcsRootId: $"PostSharpGitHub_{_projectName}{Family.VersionWithoutDots}" ) )
        {
            GenerateSnapshotDependency = false,

            // The line has no consolidated product, but it releases like one: 'Prepare Deployment' advances the
            // release branch to develop, 'Deploy [Public]' publishes from the release branch, and 'Finalize
            // Deployment' tags it and merges it back. Without this, the deployment would be expected on develop.
            PublishesFromReleaseBranch = true,
            Dependencies = [DevelopmentDependencies.PostSharpEngineering],

            // The packages this repository builds. The default is the product name followed by ".*", which would claim
            // PostSharp.Engineering.*: package source mapping would then look for the engineering packages in the
            // artifact directory, where they are not, and a restore against the generated nuget.config fails NU1101.
            PackagePatterns = ["PostSharp", "PostSharp.Redist", "PostSharp.Compiler.*", "PostSharp.Patterns.*"],
            AutoUpdateVersion = false
        };
    }
}
