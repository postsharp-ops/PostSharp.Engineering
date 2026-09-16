// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

public static partial class FoundationsDependencies
{
    // ReSharper disable once InconsistentNaming

    [PublicAPI]
    public static class V2027_0
    {
        public static ProductFamily Family { get; } = new( _projectName, "2027.0", DevelopmentDependencies.Family )
        {
            // No UpstreamProductFamily: 2027.0 is the first version line of this family.
            GitHubAppConnectionId = GitHubAppConnections.PostSharpOps
        };

        /// <summary>
        /// Foundations is the only product of its family, so the family has no per-product project level in TeamCity:
        /// the version-level project directly holds the build configurations, and its VCS root - which has the same
        /// identifier - is stored in the <c>Foundations</c> project above it.
        /// </summary>
        private static readonly TeamCityProjectId _teamCityProjectId =
            TeamCityHelper.GetSingleProductFamilyProjectId( _projectName, Family.Version );

        public static DependencyDefinition Foundations { get; } = new(
            Family,
            _projectName,
            $"develop/{Family.Version}",
            $"release/{Family.Version}",
            new GitHubRepository( "SharpCrafters.Foundations", "postsharp-ops" ),
            TeamCityHelper.CreateConfiguration( _teamCityProjectId, vcsRootId: _teamCityProjectId.Id ) )
        {
            // The family has no consolidated product, so without this the deployment would be performed from the
            // development branch.
            PublishesFromReleaseBranch = true,

            // The packages carry the full name of the product, which differs from the name used by the build system,
            // so the default patterns derived from the product name would not match them.
            PackagePatterns = ["SharpCrafters.Foundations*"],
            Dependencies = [DevelopmentDependencies.PostSharpEngineering]
        };
    }
}
