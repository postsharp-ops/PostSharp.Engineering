// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using System;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Foundations is alone in its family, its repository is named after the packages it produces rather than after the
/// product, and Metalama resolves it across family boundaries. All three are resolved by name at run time rather than
/// by the compiler, so a mistake surfaces as a missing build configuration or an unresolved dependency rather than as
/// a build break.
/// </summary>
public class FoundationsFamilyTests
{
    /// <summary>
    /// The family has no per-product project level, so the version-level project holds the build configurations
    /// directly and the product project is its parent.
    /// </summary>
    [Fact]
    public void TeamCityProject_HasNoPerProductLevel()
    {
        var ciConfiguration = FoundationsDependencies.V2027_0.Foundations.CiConfiguration;

        Assert.Equal( "Foundations_Foundations20270", ciConfiguration.ProjectId.Id );
        Assert.Equal( "Foundations", ciConfiguration.ProjectId.ParentId );
        Assert.Equal( "Foundations_Foundations20270_DebugBuild", ciConfiguration.BuildTypes.Debug );
    }

    /// <summary>
    /// The VCS root is stored in the product project, above the version project, and it carries the identifier of the
    /// version project instead of the one derived from the repository name.
    /// </summary>
    [Fact]
    public void VcsRoot_IsStoredInTheProductProject()
    {
        var definition = FoundationsDependencies.V2027_0.Foundations;

        Assert.Equal( "Foundations", definition.CiConfiguration.VcsRootProjectId );
        Assert.Equal( "Foundations_Foundations20270", TeamCityHelper.GetVcsId( definition ) );
    }

    [Fact]
    public void Repository_IsNamedAfterThePackages()
    {
        var repository = FoundationsDependencies.V2027_0.Foundations.VcsRepository;

        Assert.Equal( "SharpCrafters.Foundations", repository.Name );
        Assert.Equal( "https://github.com/postsharp-ops/SharpCrafters.Foundations.git", repository.HttpUrl );
    }

    /// <summary>
    /// The family has no consolidated product, so the product publishes from the release branch only because it sets
    /// PublishesFromReleaseBranch.
    /// </summary>
    [Fact]
    public void Product_BuildsFromDevelopAndPublishesFromRelease()
    {
        var definition = FoundationsDependencies.V2027_0.Foundations;

        Assert.Equal( "develop/2027.0", definition.Branch );
        Assert.Equal( "release/2027.0", definition.ReleaseBranch );
        Assert.Equal( "release/2027.0", definition.PublishingBranch );
    }

    [Fact]
    public void Family_HasNoUpstream()
    {
        Assert.Null( FoundationsDependencies.V2027_0.Family.UpstreamProductFamily );
        Assert.False( FoundationsDependencies.V2027_0.Family.HasConsolidatedProduct );
    }

    /// <summary>
    /// Metalama declares the Foundations family as a relative family, so it resolves the product by name. Metalama.Vsx
    /// reaches the same definition because the relative families are searched recursively.
    /// </summary>
    [Fact]
    public void Foundations_IsResolvedByNameFromTheConsumingFamilies()
    {
        Assert.True( MetalamaDependencies.V2027_0.Family.TryGetDependencyDefinition( "Foundations", out var definition ) );
        Assert.Same( FoundationsDependencies.V2027_0.Foundations, definition );

        Assert.True( MetalamaVsxDependencies.V2027_0.Family.TryGetDependencyDefinition( "Foundations", out var vsxDefinition ) );
        Assert.Same( FoundationsDependencies.V2027_0.Foundations, vsxDefinition );

        Assert.Contains(
            MetalamaDependencies.V2027_0.Metalama.Dependencies,
            d => ReferenceEquals( d.Definition, FoundationsDependencies.V2027_0.Foundations ) );
    }

    /// <summary>
    /// A package pattern configures the NuGet package source mapping, so exactly one product of the version line may
    /// claim a given pattern. The Backstage packages moved to Foundations and were renamed, so no product of the
    /// 2027.0 lines produces them any more.
    /// </summary>
    [Fact]
    public void BackstagePackages_MovedToFoundations()
    {
        Assert.Contains( "SharpCrafters.Foundations*", FoundationsDependencies.V2027_0.Foundations.PackagePatterns );

        Assert.DoesNotContain(
            MetalamaDependencies.V2027_0.Metalama.PackagePatterns,
            p => p.StartsWith( "Metalama.Backstage", StringComparison.Ordinal ) );

        // The previous version lines still build Backstage inside Metalama.
        Assert.Contains( "Metalama.Backstage*", MetalamaDependencies.V2026_1.Metalama.PackagePatterns );
    }
}
