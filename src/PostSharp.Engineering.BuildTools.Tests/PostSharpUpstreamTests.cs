// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The PostSharp lines are chained by the upstream merge, and each line owns its own repositories. Both arrangements
/// are resolved by name at run time rather than by the compiler, so a mistake surfaces as a missing build
/// configuration or a type-initialization error rather than as a build break.
/// </summary>
public class PostSharpUpstreamTests
{
    [Fact]
    public void EachLine_DeclaresThePreviousOneAsItsUpstream()
    {
        Assert.Same( PostSharpDependencies.V2024_0.Family, PostSharpDependencies.V2026_0.Family.UpstreamProductFamily );
        Assert.Same( PostSharpDependencies.V2026_0.Family, PostSharpDependencies.V2027_0.Family.UpstreamProductFamily );
        Assert.Null( PostSharpDependencies.V2024_0.Family.UpstreamProductFamily );
    }

    /// <summary>
    /// The upstream merge looks the upstream repository up by the product name of the downstream repository, so the
    /// two definitions must carry the same name. Nothing in the compiler requires that.
    /// </summary>
    [Theory]
    [InlineData( "2026.0", "develop/2024.0" )]
    [InlineData( "2027.0", "develop/2026.0" )]
    public void Upstream_IsResolvedByTheProductName( string version, string expectedUpstreamBranch )
    {
        var family = version == "2026.0" ? PostSharpDependencies.V2026_0.Family : PostSharpDependencies.V2027_0.Family;

        var productName = version == "2026.0"
            ? PostSharpDependencies.V2026_0.PostSharp.Name
            : PostSharpDependencies.V2027_0.PostSharp.Name;

        Assert.NotNull( family.UpstreamProductFamily );
        Assert.True( family.UpstreamProductFamily.TryGetDependencyDefinition( productName, out var upstreamDefinition ) );
        Assert.Equal( expectedUpstreamBranch, upstreamDefinition.Branch );
    }

    [Fact]
    public void EveryRepository_BuildsFromDevelopAndPublishesFromRelease()
    {
        AssertBranches( PostSharpDependencies.V2024_0.PostSharp, "2024.0" );
        AssertBranches( PostSharpDependencies.V2026_0.PostSharp, "2026.0" );
        AssertBranches( PostSharpDependencies.V2026_0.PostSharpDocumentation, "2026.0" );
        AssertBranches( PostSharpDependencies.V2027_0.PostSharp, "2027.0" );
        AssertBranches( PostSharpDependencies.V2027_0.PostSharpDocumentation, "2027.0" );

        static void AssertBranches( DependencyDefinition definition, string version )
        {
            Assert.Equal( $"develop/{version}", definition.Branch );
            Assert.Equal( $"release/{version}", definition.ReleaseBranch );
        }
    }

    /// <summary>
    /// Every repository of a line owns a TeamCity project, so the lines never share a build configuration. The 2024.0
    /// line is flat: the project of the line is the project of the PostSharp repository, and it is named after the
    /// repository and the version. This identifier is the one the existing project already carries.
    /// </summary>
    [Fact]
    public void EveryRepositoryOfTheFlatLine_OwnsAProjectNamedAfterItselfAndTheVersion()
    {
        Assert.Equal( "PostSharpGitHub_PostSharp20240", PostSharpDependencies.V2024_0.PostSharp.CiConfiguration.ProjectId.Id );
    }

    /// <summary>
    /// The 2026.0 and 2027.0 lines follow the Metalama arrangement instead: the project of the line holds no build
    /// configuration and contains one project per repository, named after the repository alone. Nothing derives these
    /// identifiers from the projects that exist on TeamCity, so a mismatch surfaces as a build referencing a
    /// configuration that is not there.
    /// </summary>
    [Fact]
    public void EveryRepositoryOfTheNestedLines_OwnsAProjectBeneathTheProjectOfTheLine()
    {
        Assert.Equal(
            "PostSharpGitHub_PostSharp20260_PostSharp",
            PostSharpDependencies.V2026_0.PostSharp.CiConfiguration.ProjectId.Id );

        Assert.Equal(
            "PostSharpGitHub_PostSharp20260_PostSharpDocumentation",
            PostSharpDependencies.V2026_0.PostSharpDocumentation.CiConfiguration.ProjectId.Id );

        Assert.Equal( "PostSharpGitHub_PostSharp20260", PostSharpDependencies.V2026_0.PostSharp.CiConfiguration.ProjectId.ParentId );

        Assert.Equal(
            "PostSharpGitHub_PostSharp20260",
            PostSharpDependencies.V2026_0.PostSharpDocumentation.CiConfiguration.ProjectId.ParentId );

        Assert.Equal(
            "PostSharpGitHub_PostSharp20270_PostSharp",
            PostSharpDependencies.V2027_0.PostSharp.CiConfiguration.ProjectId.Id );

        Assert.Equal(
            "PostSharpGitHub_PostSharp20270_PostSharpDocumentation",
            PostSharpDependencies.V2027_0.PostSharpDocumentation.CiConfiguration.ProjectId.Id );

        Assert.Equal( "PostSharpGitHub_PostSharp20270", PostSharpDependencies.V2027_0.PostSharp.CiConfiguration.ProjectId.ParentId );

        Assert.Equal(
            "PostSharpGitHub_PostSharp20270",
            PostSharpDependencies.V2027_0.PostSharpDocumentation.CiConfiguration.ProjectId.ParentId );
    }

    /// <summary>
    /// The VCS roots of every line are stored in the PostSharp project and are named after the repository and the
    /// version. The 2026.0 and 2027.0 lines keep that naming even though their projects are nested one level deeper,
    /// because the roots that exist on TeamCity carry those identifiers and the generated settings address them by
    /// identifier. The root of the 2026.0 documentation is the exception: its identifier carries no version.
    /// </summary>
    [Fact]
    public void TheVcsRoots_AreNamedAfterTheRepositoryAndTheVersion()
    {
        AssertVcsRoot( PostSharpDependencies.V2024_0.PostSharp, "PostSharpGitHub_PostSharp20240" );
        AssertVcsRoot( PostSharpDependencies.V2026_0.PostSharp, "PostSharpGitHub_PostSharp20260" );
        AssertVcsRoot( PostSharpDependencies.V2026_0.PostSharpDocumentation, "PostSharpGitHub_PostSharpDocumentation" );
        AssertVcsRoot( PostSharpDependencies.V2026_0.Consolidated, "PostSharpGitHub_PostSharpConsolidated20260" );
        AssertVcsRoot( PostSharpDependencies.V2027_0.PostSharp, "PostSharpGitHub_PostSharp20270" );
        AssertVcsRoot( PostSharpDependencies.V2027_0.PostSharpDocumentation, "PostSharpGitHub_PostSharpDocumentation20270" );

        static void AssertVcsRoot( DependencyDefinition definition, string expectedVcsRootId )
        {
            Assert.Equal( expectedVcsRootId, definition.CiConfiguration.VcsRootId );
            Assert.Equal( "PostSharpGitHub", definition.CiConfiguration.VcsRootProjectId );
        }
    }

    /// <summary>
    /// The documentation is written for 2026.0 onwards, so the 2024.0 line has none.
    /// </summary>
    [Fact]
    public void The20240Line_HasNoDocumentation()
        => Assert.False( PostSharpDependencies.V2024_0.Family.TryGetDependencyDefinition( "PostSharp.Documentation", out _ ) );

    /// <summary>
    /// The version bump configuration is generated only for a versioned definition, so leaving the product unversioned
    /// silently gives it no way to bump.
    /// </summary>
    [Fact]
    public void ThePostSharpProduct_IsVersioned()
    {
        Assert.True( PostSharpDependencies.V2024_0.PostSharp.IsVersioned );
        Assert.True( PostSharpDependencies.V2026_0.PostSharp.IsVersioned );
        Assert.True( PostSharpDependencies.V2027_0.PostSharp.IsVersioned );
    }

    /// <summary>
    /// The documentation is published continuously rather than released, so it is not versioned and generates no
    /// version bump configuration.
    /// </summary>
    [Fact]
    public void TheDocumentation_IsNotVersioned()
    {
        Assert.False( PostSharpDependencies.V2026_0.PostSharpDocumentation.IsVersioned );
        Assert.False( PostSharpDependencies.V2027_0.PostSharpDocumentation.IsVersioned );
        Assert.Null( PostSharpDependencies.V2026_0.PostSharpDocumentation.CiConfiguration.VersionBumpBuildType );
    }

    /// <summary>
    /// PostSharp exports only its public build -- the signed distribution -- so a consumer that resolves any other
    /// configuration points at a build type that is never produced.
    /// </summary>
    [Fact]
    public void ConsumersOfPostSharp_ResolveItsPublicBuild()
    {
        var postSharp = Assert.Single(
            PostSharpDependencies.V2026_0.PostSharpDocumentation.Dependencies,
            d => d.Definition.Name == "PostSharp" );

        Assert.Equal( BuildConfiguration.Public, postSharp.ConfigurationMapping.Debug );
        Assert.Equal( BuildConfiguration.Public, postSharp.ConfigurationMapping.Release );
        Assert.Equal( BuildConfiguration.Public, postSharp.ConfigurationMapping.Public );
    }
}
