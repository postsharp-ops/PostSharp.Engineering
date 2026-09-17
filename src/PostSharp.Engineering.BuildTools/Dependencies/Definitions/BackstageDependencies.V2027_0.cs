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
        /// The GitHub organization of every repository of this family. It is not the one of the Metalama and PostSharp
        /// families, which is why <see cref="Family"/> overrides the GitHub App connection.
        /// </summary>
        private const string _gitHubOwner = "postsharp-ops";

        /// <summary>
        /// Gets the TeamCity project of a product of this line. The family is laid out flat, as the PostSharp 2024.0
        /// and 2026.0 lines are: it has no project for the version line, so each product owns a project named after
        /// itself and the version directly beneath the <c>Backstage</c> project, which is also where the VCS roots of
        /// the family are stored.
        /// </summary>
        private static TeamCityProjectId GetProjectId( string dependencyName )
            => TeamCityHelper.GetProjectIdWithParentProjectId( $"{dependencyName} {Family.Version}", _projectName );

        /// <summary>
        /// A repository of this line. It builds from the development branch and owns a TeamCity project and a VCS root
        /// that are both named after the product and the version. The VCS root is named that way rather than after the
        /// repository, which is the default, because a repository has one branch per version line and therefore needs
        /// one VCS root per line.
        /// </summary>
        private class BackstageDependencyDefinition : DependencyDefinition
        {
            public BackstageDependencyDefinition( string dependencyName, string repositoryName, bool hasVersionBump = true )
                : base(
                    Family,
                    dependencyName,
                    $"develop/{Family.Version}",
                    $"release/{Family.Version}",
                    new GitHubRepository( repositoryName, _gitHubOwner ),
                    TeamCityHelper.CreateConfiguration(
                        GetProjectId( dependencyName ),
                        hasVersionBump,
                        vcsRootId: GetProjectId( dependencyName ).Id ) ) { }
        }

        /// <summary>
        /// The shared licensing, telemetry and diagnostics library that both product lines are built on. The repository
        /// is named after the packages it produces rather than after the product.
        /// </summary>
        public static DependencyDefinition Backstage { get; } =
            new BackstageDependencyDefinition( _projectName, $"SharpCrafters.{_projectName}", hasVersionBump: false )
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

        /// <summary>
        /// The license server. Unlike <see cref="Backstage"/>, it is a product of its own: no consolidated product
        /// releases it, so it keeps its own version bump and deploys from the development branch.
        /// </summary>
        public static DependencyDefinition BackstageLicenseServer { get; } =
            new BackstageDependencyDefinition( $"{_projectName}.LicenseServer", $"SharpCrafters.{_projectName}.LicenseServer" )
            {
                // As for Backstage, the packages carry the vendor prefix that the product name used by the build system
                // omits. The pattern overlaps the "SharpCrafters.Backstage*" of Backstage, which is what package source
                // mapping resolves by longest prefix, so these packages are taken from this product and not from that one.
                PackagePatterns = ["SharpCrafters.Backstage.LicenseServer*"],
                Dependencies = [DevelopmentDependencies.PostSharpEngineering, Backstage]
            };
    }
}
