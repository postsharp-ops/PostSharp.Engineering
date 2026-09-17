// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The 2027.0 line of PostSharp is the first one with a consolidated product. Declaring one changes how the whole line
/// is bumped, published and laid out on TeamCity, and none of that is checked by the compiler.
/// </summary>
public class PostSharpConsolidatedTests
{
    /// <summary>
    /// The consolidated product is what makes the line bump and deploy as a whole, and what makes its products publish
    /// from the release branch rather than from the development branch.
    /// </summary>
    [Fact]
    public void The20270Line_IsConsolidatedAndThePreviousOnesAreNot()
    {
        Assert.True( PostSharpDependencies.V2027_0.Family.HasConsolidatedProduct );
        Assert.Equal( "PostSharp.Consolidated", PostSharpDependencies.V2027_0.Family.ConsolidatedProjectName );
        Assert.True( PostSharpDependencies.V2027_0.Consolidated.IsConsolidated );

        Assert.False( PostSharpDependencies.V2024_0.Family.HasConsolidatedProduct );
        Assert.False( PostSharpDependencies.V2026_0.Family.HasConsolidatedProduct );
    }

    /// <summary>
    /// Only a consolidated line publishes from the release branch. The 2026.0 line has no consolidated product, so its
    /// products still publish from the development branch.
    /// </summary>
    [Fact]
    public void TheProductsOf20270_PublishFromTheReleaseBranch()
    {
        Assert.Equal( "release/2027.0", PostSharpDependencies.V2027_0.PostSharp.PublishingBranch );
        Assert.Equal( "release/2027.0", PostSharpDependencies.V2027_0.PostSharpDocumentation.PublishingBranch );
        Assert.Equal( "develop/2026.0", PostSharpDependencies.V2026_0.PostSharp.PublishingBranch );
    }

    /// <summary>
    /// The consolidated build has no source code, so it is not versioned, and it owns a TeamCity project beneath the
    /// project of the line and a VCS root named after the repository and the version, as the other repositories of the
    /// line do.
    /// </summary>
    [Fact]
    public void TheConsolidatedProduct_FollowsTheLayoutOfTheLine()
    {
        var definition = PostSharpDependencies.V2027_0.Consolidated;

        Assert.False( definition.IsVersioned );
        Assert.Equal( "PostSharpGitHub_PostSharp20270_PostSharpConsolidated", definition.CiConfiguration.ProjectId.Id );
        Assert.Equal( "PostSharpGitHub_PostSharp20270", definition.CiConfiguration.ProjectId.ParentId );
        Assert.Equal( "PostSharpGitHub_PostSharpConsolidated20270", definition.CiConfiguration.VcsRootId );
        Assert.Equal( "PostSharpGitHub", definition.CiConfiguration.VcsRootProjectId );
        Assert.Equal( "https://github.com/PostSharp/PostSharp.Consolidated.git", definition.VcsRepository.HttpUrl );
    }

    /// <summary>
    /// The three repositories the line consolidates. They are source dependencies, because the consolidated build
    /// checks each of them out to bump its version and to merge its development branch into its release branch, and
    /// they are also ordinary dependencies, because the build consumes the packages they produce.
    /// </summary>
    [Fact]
    public void TheConsolidatedProduct_ConsolidatesBackstageAndTheProductsOfTheLine()
    {
        var definition = PostSharpDependencies.V2027_0.Consolidated;

        Assert.Equal(
            [BackstageDependencies.V2027_0.Backstage, PostSharpDependencies.V2027_0.PostSharp, PostSharpDependencies.V2027_0.PostSharpDocumentation],
            definition.SourceDependencies );

        Assert.Equal(
            ["PostSharp.Engineering", "Backstage", "PostSharp", "PostSharp.Documentation"],
            definition.Dependencies.Select( d => d.Definition.Name ) );
    }

    /// <summary>
    /// Backstage is in a family of its own, so the line declares that family as a relative one. Without it the
    /// dependency cannot be resolved by name, which is how the products of the line address it at run time.
    /// </summary>
    [Fact]
    public void Backstage_IsResolvedByNameFromThe20270Line()
    {
        Assert.True( PostSharpDependencies.V2027_0.Family.TryGetDependencyDefinition( "Backstage", out var definition ) );
        Assert.Same( BackstageDependencies.V2027_0.Backstage, definition );
    }

    /// <summary>
    /// The consolidated build chains the builds it consolidates through TeamCity snapshot dependencies, and a
    /// dependency that generates none is not chained. This is the difference between this line and the previous ones,
    /// where the PostSharp packages are restored from the package feed instead.
    /// </summary>
    [Fact]
    public void TheProductsOf20270_AreChained()
    {
        Assert.True( PostSharpDependencies.V2027_0.PostSharp.GenerateSnapshotDependency );
        Assert.True( PostSharpDependencies.V2027_0.PostSharpDocumentation.GenerateSnapshotDependency );
        Assert.True( BackstageDependencies.V2027_0.Backstage.GenerateSnapshotDependency );

        Assert.False( PostSharpDependencies.V2026_0.PostSharp.GenerateSnapshotDependency );
    }

    /// <summary>
    /// The Metalama 2027.0 line is built and deployed against the same Backstage, so its consolidated product carries
    /// it too.
    /// </summary>
    [Fact]
    public void TheMetalamaConsolidatedProduct_AlsoConsolidatesBackstage()
    {
        var definition = MetalamaDependencies.V2027_0.Consolidated;

        Assert.Contains( BackstageDependencies.V2027_0.Backstage, definition.SourceDependencies );
        Assert.Contains( definition.Dependencies, d => ReferenceEquals( d.Definition, BackstageDependencies.V2027_0.Backstage ) );
    }

    /// <summary>
    /// The flag says that a consolidated product of another family releases the product. Nothing derives it, so it can
    /// disagree with the source dependencies that actually consolidate the product, and a disagreement is silent: a
    /// product that sets it without being consolidated anywhere is never bumped at all, and one that is consolidated
    /// without setting it is bumped both by its own configuration and by the consolidated product.
    /// </summary>
    [Fact]
    public void TheFlag_AgreesWithTheProductsThatActuallyConsolidate()
    {
        var consolidatedByAnotherFamily = AllDefinitions()
            .Where( d => d.IsConsolidatedByAnotherFamily )
            .ToHashSet();

        var consolidatedElsewhere = AllDefinitions()
            .Where( d => d.IsConsolidated )
            .SelectMany( d => d.SourceDependencies.Where( s => s.ProductFamily != d.ProductFamily ) )
            .ToHashSet();

        Assert.Equal(
            consolidatedByAnotherFamily.Select( d => d.Name ).OrderBy( n => n ),
            consolidatedElsewhere.Select( d => d.Name ).OrderBy( n => n ) );

        // Guards the sweep itself: Backstage is the case the flag exists for.
        Assert.Contains( BackstageDependencies.V2027_0.Backstage, consolidatedByAnotherFamily );
    }

    /// <summary>
    /// Enumerating the whole assembly would load types that reference Microsoft.Build, which the test host cannot
    /// resolve, so the sweep starts from the definition classes and walks their nested version classes.
    /// </summary>
    private static IEnumerable<DependencyDefinition> AllDefinitions()
        => new[]
            {
                typeof(DevelopmentDependencies), typeof(BusinessSystemsDependencies), typeof(TemplateDependencies), typeof(BackstageDependencies),
                typeof(PostSharpDependencies), typeof(MetalamaDependencies), typeof(MetalamaVsxDependencies), typeof(TestDependencies)
            }
            .SelectMany( t => t.GetNestedTypes( BindingFlags.Public ).Append( t ) )
            .SelectMany( t => t.GetProperties( BindingFlags.Public | BindingFlags.Static ) )
            .Where( p => p.PropertyType.IsAssignableTo( typeof(DependencyDefinition) ) )
            .Select( p => (DependencyDefinition?) p.GetValue( null ) )
            .Where( d => d != null )
            .Select( d => d! )
            .Distinct();

    /// <summary>
    /// A package pattern configures the NuGet package source mapping, so exactly one product of a version line may
    /// claim a given pattern. Backstage takes part in two consolidated builds, so its patterns must clash with neither
    /// line.
    /// </summary>
    [Fact]
    public void BackstagePackages_ClashWithNoProductOfEitherLine()
    {
        AssertNoClash( PostSharpDependencies.V2027_0.Consolidated );
        AssertNoClash( MetalamaDependencies.V2027_0.Consolidated );

        static void AssertNoClash( DependencyDefinition consolidated )
        {
            var backstagePatterns = BackstageDependencies.V2027_0.Backstage.PackagePatterns;

            foreach ( var sibling in consolidated.SourceDependencies.Where( d => d != BackstageDependencies.V2027_0.Backstage ) )
            {
                Assert.Empty( sibling.PackagePatterns.Intersect( backstagePatterns ) );
            }
        }
    }
}
