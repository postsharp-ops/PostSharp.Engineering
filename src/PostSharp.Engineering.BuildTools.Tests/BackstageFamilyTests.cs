// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using System;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The repositories of this family are named after the packages they produce rather than after the product, the family
/// is laid out flat on TeamCity, and Metalama resolves Backstage across family boundaries. All three are resolved by
/// name at run time rather than by the compiler, so a mistake surfaces as a missing build configuration or an
/// unresolved dependency rather than as a build break.
/// </summary>
public class BackstageFamilyTests
{
    /// <summary>
    /// The family has no project for the version line: every product owns a project named after itself and the
    /// version directly beneath the family project, as the PostSharp 2024.0 and 2026.0 lines do. These identifiers
    /// are the ones the TeamCity server carries, and every repository that depends on a product of this family
    /// addresses its build configurations by them, so they are asserted literally.
    /// </summary>
    [Fact]
    public void TeamCityProjects_AreFlat()
    {
        AssertProject( BackstageDependencies.V2027_0.Backstage, "Backstage_Backstage20270" );
        AssertProject( BackstageDependencies.V2027_0.BackstageLicenseServer, "Backstage_BackstageLicenseServer20270" );

        static void AssertProject( DependencyDefinition definition, string expectedProjectId )
        {
            var ciConfiguration = definition.CiConfiguration;

            Assert.Equal( expectedProjectId, ciConfiguration.ProjectId.Id );
            Assert.Equal( "Backstage", ciConfiguration.ProjectId.ParentId );
            Assert.Equal( $"{expectedProjectId}_DebugBuild", ciConfiguration.BuildTypes.Debug );
        }
    }

    /// <summary>
    /// The VCS root of a product is stored in the family project, above the project of the product, and it carries the
    /// identifier of that project instead of the one derived from the repository name. The repository holds one branch
    /// per version line, so the identifier has to carry the version, which the derived one does not.
    /// </summary>
    [Fact]
    public void VcsRoot_IsStoredInTheFamilyProjectAndCarriesTheVersion()
    {
        AssertVcsRoot( BackstageDependencies.V2027_0.Backstage, "Backstage_Backstage20270" );
        AssertVcsRoot( BackstageDependencies.V2027_0.BackstageLicenseServer, "Backstage_BackstageLicenseServer20270" );

        static void AssertVcsRoot( DependencyDefinition definition, string expectedVcsRootId )
        {
            Assert.Equal( "Backstage", definition.CiConfiguration.VcsRootProjectId );
            Assert.Equal( expectedVcsRootId, TeamCityHelper.GetVcsId( definition ) );
        }
    }

    /// <summary>
    /// The repositories carry the vendor prefix that the product name used by the build system omits, and they are all
    /// in the organization the family declares its GitHub App connection for.
    /// </summary>
    [Fact]
    public void Repositories_AreNamedAfterThePackages()
    {
        AssertRepository( BackstageDependencies.V2027_0.Backstage, "SharpCrafters.Backstage" );
        AssertRepository( BackstageDependencies.V2027_0.BackstageLicenseServer, "SharpCrafters.Backstage.LicenseServer" );

        static void AssertRepository( DependencyDefinition definition, string expectedName )
        {
            Assert.Equal( expectedName, definition.VcsRepository.Name );
            Assert.Equal( $"https://github.com/postsharp-ops/{expectedName}.git", definition.VcsRepository.HttpUrl );
            Assert.Equal( GitHubAppConnections.PostSharpOps, definition.EffectiveGitHubAppConnectionId );
        }
    }

    /// <summary>
    /// The family has no consolidated product of its own, so the product publishes from the release branch only because
    /// consolidated products of other families release it.
    /// </summary>
    [Fact]
    public void Product_BuildsFromDevelopAndPublishesFromRelease()
    {
        var definition = BackstageDependencies.V2027_0.Backstage;

        Assert.Equal( "develop/2027.0", definition.Branch );
        Assert.Equal( "release/2027.0", definition.ReleaseBranch );
        Assert.Equal( "release/2027.0", definition.PublishingBranch );
    }

    /// <summary>
    /// The product is released only by the consolidated products of the Metalama and the PostSharp lines, so it has no
    /// version bump of its own. It stays versioned, because its version is what its consumers pin.
    /// </summary>
    [Fact]
    public void Product_IsVersionedButHasNoVersionBumpOfItsOwn()
    {
        var definition = BackstageDependencies.V2027_0.Backstage;

        Assert.True( definition.IsVersioned );
        Assert.True( definition.IsConsolidatedByAnotherFamily );
        Assert.True( definition.IsPartOfConsolidatedBuild );
        Assert.Null( definition.CiConfiguration.VersionBumpBuildType );
    }

    /// <summary>
    /// The license server is released on its own: no consolidated product lists it, so it keeps the version bump and
    /// the publishing from the development branch that an ordinary product has. This is what separates it from
    /// Backstage, which sits in the same family.
    /// </summary>
    [Fact]
    public void LicenseServer_IsReleasedOnItsOwn()
    {
        var definition = BackstageDependencies.V2027_0.BackstageLicenseServer;

        Assert.True( definition.IsVersioned );
        Assert.False( definition.IsConsolidatedByAnotherFamily );
        Assert.False( definition.IsPartOfConsolidatedBuild );
        Assert.Equal( "Backstage_BackstageLicenseServer20270_VersionBump", definition.CiConfiguration.VersionBumpBuildType );

        Assert.Equal( "develop/2027.0", definition.Branch );
        Assert.Equal( "release/2027.0", definition.ReleaseBranch );
        Assert.Equal( "develop/2027.0", definition.PublishingBranch );
    }

    /// <summary>
    /// The license server is built against Backstage. The reference is what puts the Backstage version in its
    /// dependency file and chains the two builds on TeamCity.
    /// </summary>
    [Fact]
    public void LicenseServer_DependsOnBackstage()
    {
        Assert.Contains(
            BackstageDependencies.V2027_0.BackstageLicenseServer.Dependencies,
            d => ReferenceEquals( d.Definition, BackstageDependencies.V2027_0.Backstage ) );
    }

    /// <summary>
    /// Both products of the family claim a pattern starting with "SharpCrafters.Backstage", so the pattern of the
    /// license server has to be the longer one: package source mapping resolves by longest prefix, and a package of
    /// the license server that also matched only the pattern of Backstage would be looked for in the artifacts of
    /// Backstage, where it is not.
    /// </summary>
    [Fact]
    public void LicenseServerPackages_AreMoreSpecificThanTheBackstageOnes()
    {
        var licenseServerPattern = Assert.Single( BackstageDependencies.V2027_0.BackstageLicenseServer.PackagePatterns );

        Assert.Equal( "SharpCrafters.Backstage.LicenseServer*", licenseServerPattern );

        var backstagePattern = Assert.Single(
            BackstageDependencies.V2027_0.Backstage.PackagePatterns,
            p => licenseServerPattern.StartsWith( p.TrimEnd( '*' ), StringComparison.Ordinal ) );

        Assert.True(
            licenseServerPattern.TrimEnd( '*' ).Length > backstagePattern.TrimEnd( '*' ).Length,
            $"'{licenseServerPattern}' is not more specific than '{backstagePattern}'." );
    }

    /// <summary>
    /// The family holds more than one product, so a product is resolved by its own name and not by the name of the
    /// family, which happens to be the name of one of them.
    /// </summary>
    [Fact]
    public void Products_AreResolvedByName()
    {
        Assert.True( BackstageDependencies.V2027_0.Family.TryGetDependencyDefinition( "Backstage", out var backstage ) );
        Assert.Same( BackstageDependencies.V2027_0.Backstage, backstage );

        Assert.True(
            BackstageDependencies.V2027_0.Family.TryGetDependencyDefinition( "Backstage.LicenseServer", out var licenseServer ) );

        Assert.Same( BackstageDependencies.V2027_0.BackstageLicenseServer, licenseServer );
    }

    [Fact]
    public void Family_HasNoUpstream()
    {
        Assert.Null( BackstageDependencies.V2027_0.Family.UpstreamProductFamily );
        Assert.False( BackstageDependencies.V2027_0.Family.HasConsolidatedProduct );
    }

    /// <summary>
    /// Metalama declares the Backstage family as a relative family, so it resolves the product by name. Metalama.Vsx
    /// reaches the same definition because the relative families are searched recursively.
    /// </summary>
    [Fact]
    public void Backstage_IsResolvedByNameFromTheConsumingFamilies()
    {
        Assert.True( MetalamaDependencies.V2027_0.Family.TryGetDependencyDefinition( "Backstage", out var definition ) );
        Assert.Same( BackstageDependencies.V2027_0.Backstage, definition );

        Assert.True( MetalamaVsxDependencies.V2027_0.Family.TryGetDependencyDefinition( "Backstage", out var vsxDefinition ) );
        Assert.Same( BackstageDependencies.V2027_0.Backstage, vsxDefinition );

        Assert.Contains(
            MetalamaDependencies.V2027_0.Metalama.Dependencies,
            d => ReferenceEquals( d.Definition, BackstageDependencies.V2027_0.Backstage ) );
    }

    /// <summary>
    /// A package pattern configures the NuGet package source mapping, so exactly one product of the version line may
    /// claim a given pattern. The Backstage packages moved to their own product, so no product of the Metalama
    /// 2027.0 line produces them any more.
    /// </summary>
    [Fact]
    public void BackstagePackages_MovedToTheirOwnProduct()
    {
        Assert.Contains( "SharpCrafters.Backstage*", BackstageDependencies.V2027_0.Backstage.PackagePatterns );

        Assert.DoesNotContain(
            MetalamaDependencies.V2027_0.Metalama.PackagePatterns,
            p => p.StartsWith( "Metalama.Backstage", StringComparison.Ordinal ) );

        // The previous version lines still build Backstage inside Metalama.
        Assert.Contains( "Metalama.Backstage*", MetalamaDependencies.V2026_1.Metalama.PackagePatterns );
    }
}
