// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

public static partial class BackstageDependencies
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
        /// Backstage is the only product of its family, so the family has no per-product project level in TeamCity:
        /// the version-level project directly holds the build configurations, and its VCS root - which has the same
        /// identifier - is stored in the <c>Backstage</c> project above it.
        /// </summary>
        private static readonly TeamCityProjectId _teamCityProjectId =
            TeamCityHelper.GetSingleProductFamilyProjectId( _projectName, Family.Version );

        public static DependencyDefinition Backstage { get; } = new(
            Family,
            _projectName,
            $"develop/{Family.Version}",
            $"release/{Family.Version}",
            new GitHubRepository( "SharpCrafters.Backstage", "postsharp-ops" ),
            TeamCityHelper.CreateConfiguration( _teamCityProjectId, hasVersionBump: false, vcsRootId: _teamCityProjectId.Id ) )
        {
            // The product is never released on its own: the consolidated products of the Metalama and the PostSharp
            // 2027.0 lines both build, bump and deploy it. This is what removes its own version bump configuration and
            // makes it deploy from the release branch, as the members of a consolidated family do.
            IsConsolidatedByAnotherFamily = true,

            // The neutral packages carry the vendor prefix, the shared library carries no product name, and the product
            // customizations carry the product name, so none of them matches the default patterns derived from the name
            // used by the build system.
            PackagePatterns = ["SharpCrafters.Backstage*", "SharpCrafters.Common*", "Metalama.Backstage*"],
            Dependencies = [DevelopmentDependencies.PostSharpEngineering]
        };
    }
}
