// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using System;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Backstage is alone in its family, its repository is named after the packages it produces rather than after the
/// product, and Metalama resolves it across family boundaries. All three are resolved by name at run time rather than
/// by the compiler, so a mistake surfaces as a missing build configuration or an unresolved dependency rather than as
/// a build break.
/// </summary>
public class BackstageFamilyTests
{
    /// <summary>
    /// The family has no per-product project level, so the version-level project holds the build configurations
    /// directly and the product project is its parent.
    /// </summary>
    [Fact]
    public void TeamCityProject_HasNoPerProductLevel()
    {
        var ciConfiguration = BackstageDependencies.V2027_0.Backstage.CiConfiguration;

        Assert.Equal( "Backstage_Backstage20270", ciConfiguration.ProjectId.Id );
        Assert.Equal( "Backstage", ciConfiguration.ProjectId.ParentId );
        Assert.Equal( "Backstage_Backstage20270_DebugBuild", ciConfiguration.BuildTypes.Debug );
    }

    /// <summary>
    /// The VCS root is stored in the product project, above the version project, and it carries the identifier of the
    /// version project instead of the one derived from the repository name.
    /// </summary>
    [Fact]
    public void VcsRoot_IsStoredInTheProductProject()
    {
        var definition = BackstageDependencies.V2027_0.Backstage;

        Assert.Equal( "Backstage", definition.CiConfiguration.VcsRootProjectId );
        Assert.Equal( "Backstage_Backstage20270", TeamCityHelper.GetVcsId( definition ) );
    }

    [Fact]
    public void Repository_IsNamedAfterThePackages()
    {
        var repository = BackstageDependencies.V2027_0.Backstage.VcsRepository;

        Assert.Equal( "SharpCrafters.Backstage", repository.Name );
        Assert.Equal( "https://github.com/postsharp-ops/SharpCrafters.Backstage.git", repository.HttpUrl );
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
