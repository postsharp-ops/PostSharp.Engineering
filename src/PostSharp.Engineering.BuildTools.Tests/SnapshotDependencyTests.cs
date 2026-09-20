// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using MetalamaDependencies = PostSharp.Engineering.BuildTools.Dependencies.Definitions.MetalamaDependencies;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// A build configuration can depend on several other build configurations of the same product, each with its own
/// artifact rules.
/// </summary>
/// <remarks>
/// The case that prompted this: PostSharp's public build is its signed distribution, which consumes the archives to
/// be signed from one configuration and the unsigned baseline it is compared against from another, under different
/// rules and different clean-destination flags. The single slot that came before could name only one of them.
/// </remarks>
public sealed class SnapshotDependencyTests
{
    /// <summary>
    /// A product with the two intermediate configurations that a dependency can name, plus whatever the test adds.
    /// </summary>
    private static Product CreateProduct( params AdditionalCiBuildConfiguration[] configurations )
        => new( MetalamaDependencies.V2026_1.Metalama )
        {
            AdditionalCiBuildConfigurations =
            [
                new PowershellAdditionalCiBuildConfiguration( "BuildArtifacts", "Build artifacts", "Build.ps1", "build" ),
                new PowershellAdditionalCiBuildConfiguration( "BuildDistribution", "Build distribution", "Build.ps1", "dist" ),
                ..configurations
            ]
        };

    private static string GenerateCode( AdditionalCiBuildConfiguration configuration )
    {
        var teamCityConfiguration = configuration.TeamCityBuildConfiguration(
            new ProductProperties( CreateProduct( configuration ) ),
            new Dictionary<BuildConfiguration, TeamCityBuildConfiguration>() );

        var writer = new StringWriter();
        teamCityConfiguration.GenerateTeamcityCode( writer );

        return writer.ToString();
    }

    private static bool IsValid( params AdditionalCiBuildConfiguration[] configurations )
        => SnapshotDependencyGraph.TryValidate( new ConsoleHelper(), CreateProduct( configurations ) );

    /// <summary>
    /// The obsolete members and the collection are two ways of declaring the same thing, so they must emit the same
    /// text. This is what makes that claim checkable rather than merely asserted in a comment.
    /// </summary>
    [Fact]
    public void TheObsoleteSpellingAndTheNewOneEmitIdenticalCode()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var obsolete = GenerateCode(
            new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
            {
                BuildSnapshotDependency = BuildConfiguration.Public,
                BuildSnapshotDependencyId = "BuildArtifacts",
                DependencyArtifactRules = @"+:a/**/*=>a\n+:b-*.7z!**=>",
                CleanDependencyDestination = false
            } );
#pragma warning restore CS0618 // Type or member is obsolete

        var current = GenerateCode(
            new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
            {
                BuildSnapshotDependency = BuildConfiguration.Public,
                SnapshotDependencies =
                [
                    new SnapshotDependency( "BuildArtifacts" )
                    {
                        ArtifactRules = ["+:a/**/*=>a", "+:b-*.7z!**=>"], CleanDestination = false
                    }
                ]
            } );

        Assert.Equal( obsolete, current );
    }

    /// <summary>
    /// Two dependencies, each with rules and a clean-destination flag of its own. The rules of one must not reach
    /// the other, which one pre-joined string could not have kept apart.
    /// </summary>
    [Fact]
    public void SeveralDependenciesEmitSeveralArtifactBlocks()
    {
        var code = GenerateCode(
            new PowershellAdditionalCiBuildConfiguration( "Signed", "Signed distribution", "make.ps1", "dist" )
            {
                BuildSnapshotDependency = BuildConfiguration.Public,
                SnapshotDependencies =
                [
                    new SnapshotDependency( "BuildArtifacts" ) { ArtifactRules = ["+:x-*.7z!**=>"], CleanDestination = false },
                    new SnapshotDependency( "BuildDistribution" ) { ArtifactRules = ["+:Release-*.7z!**=>unsigned-baseline"] }
                ]
            } );

        Assert.Contains( "snapshot(BuildArtifacts)", code, StringComparison.Ordinal );
        Assert.Contains( "snapshot(BuildDistribution)", code, StringComparison.Ordinal );
        Assert.Contains( "artifactRules = \"+:x-*.7z!**=>\"", code, StringComparison.Ordinal );
        Assert.Contains( "artifactRules = \"+:Release-*.7z!**=>unsigned-baseline\"", code, StringComparison.Ordinal );
        Assert.Contains( "cleanDestination = false", code, StringComparison.Ordinal );
        Assert.Contains( "cleanDestination = true", code, StringComparison.Ordinal );
    }

    /// <summary>
    /// Several rules of one dependency are joined by an escaped newline, because they land in a single-line Kotlin
    /// string. A real newline there would terminate the string and the settings file would not compile.
    /// </summary>
    [Fact]
    public void SeveralRulesOfOneDependencyAreJoinedByAnEscapedNewline()
    {
        var code = GenerateCode(
            new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
            {
                BuildSnapshotDependency = BuildConfiguration.Public,
                SnapshotDependencies = [new SnapshotDependency( "BuildArtifacts" ) { ArtifactRules = ["+:a=>a", "+:b=>b"] }]
            } );

        Assert.Contains( @"artifactRules = ""+:a=>a\n+:b=>b""", code, StringComparison.Ordinal );
    }

    /// <summary>
    /// A build configuration of the same product is referenced by the object name of its build type, never by an
    /// absolute TeamCity identifier, which addresses another project.
    /// </summary>
    [Fact]
    public void ADependencyOnTheSameProductIsEmittedBare()
    {
        var code = GenerateCode(
            new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
            {
                BuildSnapshotDependency = BuildConfiguration.Public, SnapshotDependencies = [new SnapshotDependency( "BuildArtifacts" )]
            } );

        Assert.Contains( "snapshot(BuildArtifacts)", code, StringComparison.Ordinal );
        Assert.DoesNotContain( "AbsoluteId(\"BuildArtifacts\")", code, StringComparison.Ordinal );
    }

    /// <summary>
    /// An empty rule set waits for the target without downloading anything from it.
    /// </summary>
    [Fact]
    public void AnEmptyRuleSetIsAnOrderingDependencyOnly()
    {
        var code = GenerateCode(
            new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
            {
                BuildSnapshotDependency = BuildConfiguration.Public,
                SnapshotDependencies = [new SnapshotDependency( "BuildArtifacts" ) { ArtifactRules = [] }]
            } );

        Assert.Contains( "snapshot(BuildArtifacts)", code, StringComparison.Ordinal );
        Assert.DoesNotContain( "artifacts(BuildArtifacts)", code, StringComparison.Ordinal );
    }

    /// <summary>
    /// The form that nine of the fifteen declaration sites use, which this change leaves untouched.
    /// </summary>
    [Fact]
    public void TheBuildSnapshotDependencyShortcutStillWorksAlone()
    {
        var code = GenerateCode(
            new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
            {
                BuildSnapshotDependency = BuildConfiguration.Debug
            } );

        Assert.Contains( "snapshot(DebugBuild)", code, StringComparison.Ordinal );
        Assert.Contains( "artifacts(DebugBuild)", code, StringComparison.Ordinal );
    }

    [Fact]
    public void AConfigurationWithoutDependenciesGetsNone()
        => Assert.DoesNotContain(
            "snapshot(",
            GenerateCode( new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" ) ),
            StringComparison.Ordinal );

    /// <summary>
    /// The first stage of a pipeline compiles the product, so it needs the artifacts of the products the product is
    /// built against, although it waits for no stage of its own product.
    /// </summary>
    /// <remarks>
    /// The two kinds of dependency used to be emitted together, so a first stage received neither and compiled
    /// against whatever the package feeds held. For a dependency that is published to no feed, that is nothing.
    /// </remarks>
    [Fact]
    public void AStageThatBuildsTheProductTakesTheArtifactsOfItsDependencies()
    {
        var code = GenerateCode(
            new PowershellAdditionalCiBuildConfiguration( "Build", "Build the product", "Build.ps1", "build" )
            {
                ConsumesProductDependencies = true
            } );

        Assert.Contains( "=>dependencies/Metalama.Compiler", code, StringComparison.Ordinal );

        // Both generated steps read a file that a stage of this product publishes, and this configuration waits for
        // no such stage. The build it runs writes both files itself.
        Assert.DoesNotContain( "CopyNuGetConfig", code, StringComparison.Ordinal );
        Assert.DoesNotContain( "CreateVersionsFile", code, StringComparison.Ordinal );
    }

    /// <summary>
    /// The artifacts of the products this product depends on are not taken by default. A configuration that neither
    /// builds nor tests the product would otherwise wait for a full build of every dependency and use none of it.
    /// </summary>
    [Fact]
    public void AConfigurationThatAsksForNothingGetsNoProductDependency()
        => Assert.DoesNotContain(
            "=>dependencies/",
            GenerateCode( new PowershellAdditionalCiBuildConfiguration( "Bump", "Version bump", "Build.ps1", "bump" ) ),
            StringComparison.Ordinal );

    /// <summary>
    /// A configuration that continues a stage of its own product keeps taking the artifacts of the products the
    /// product is built against, and keeps the two steps that prepare its checkout.
    /// </summary>
    [Fact]
    public void AConfigurationThatContinuesAStageTakesBothKindsOfDependency()
    {
        var code = GenerateCode(
            new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "make.ps1", "test" )
            {
                SnapshotDependencies = [new SnapshotDependency( "BuildArtifacts" )]
            } );

        Assert.Contains( "snapshot(BuildArtifacts)", code, StringComparison.Ordinal );
        Assert.Contains( "=>dependencies/Metalama.Compiler", code, StringComparison.Ordinal );
        Assert.Contains( "CopyNuGetConfig", code, StringComparison.Ordinal );
        Assert.Contains( "CreateVersionsFile", code, StringComparison.Ordinal );
    }

    [Fact]
    public void SettingBothSpellingsIsRejected()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var cell = new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
        {
            BuildSnapshotDependency = BuildConfiguration.Public,
            BuildSnapshotDependencyId = "BuildArtifacts",
            SnapshotDependencies = [new SnapshotDependency( "BuildDistribution" )]
        };
#pragma warning restore CS0618 // Type or member is obsolete

        Assert.False( IsValid( cell ) );
    }

    [Fact]
    public void AnUnknownIdentifierIsRejected()
        => Assert.False(
            IsValid(
                new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
                {
                    BuildSnapshotDependency = BuildConfiguration.Public, SnapshotDependencies = [new SnapshotDependency( "BuildArtefacts" )]
                } ) );

    [Fact]
    public void AKnownIdentifierIsAccepted()
        => Assert.True(
            IsValid(
                new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
                {
                    BuildSnapshotDependency = BuildConfiguration.Public, SnapshotDependencies = [new SnapshotDependency( "BuildArtifacts" )]
                } ) );

    /// <summary>
    /// The layout selector and the dependency list are separate members, so nothing but this check stops them from
    /// naming different build configurations. The generated steps would then read a directory the build never
    /// downloaded, and the failure would be a missing file with nothing to say why.
    /// </summary>
    [Fact]
    public void ALayoutThatNamesAConfigurationNoDependencyTargetsIsRejected()
        => Assert.False(
            IsValid(
                new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
                {
                    BuildSnapshotDependency = BuildConfiguration.Public,
                    SnapshotDependencies = [new SnapshotDependency( BuildConfiguration.Debug )]
                } ) );

    [Fact]
    public void ALayoutLeftUnsetBesideABuildConfigurationDependencyIsRejected()
        => Assert.False(
            IsValid(
                new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
                {
                    SnapshotDependencies = [new SnapshotDependency( BuildConfiguration.Debug )]
                } ) );

    /// <summary>
    /// Every dependency is on an additional configuration, so the layout falls back to the public one. That is the
    /// documented behaviour and what a product with intermediate configurations relies on.
    /// </summary>
    [Fact]
    public void ALayoutLeftUnsetBesideAnAdditionalConfigurationDependencyIsAccepted()
        => Assert.True(
            IsValid(
                new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
                {
                    SnapshotDependencies = [new SnapshotDependency( "BuildArtifacts" )]
                } ) );

    /// <summary>
    /// A dependency that downloads nothing is an ordering constraint, which is the snapshot dependency itself, and
    /// reusing the last successful build suppresses that snapshot. Together they emit neither block, so the
    /// dependency would vanish from the generated configuration without a word.
    /// </summary>
    [Fact]
    public void AnOrderingOnlyDependencyThatReusesTheLastSuccessfulBuildIsRejected()
    {
        // Declared on the dependency.
        Assert.False(
            IsValid(
                new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
                {
                    SnapshotDependencies = [new SnapshotDependency( "BuildArtifacts" ) { ArtifactRules = [], ReuseLastSuccessfulBuild = true }]
                } ) );

        // Inherited from the configuration, which is how a nightly job sets it.
        Assert.False(
            IsValid(
                new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
                {
                    ReuseLastSuccessfulBuild = true,
                    SnapshotDependencies = [new SnapshotDependency( "BuildArtifacts" ) { ArtifactRules = [] }]
                } ) );

        // Either one alone is meaningful.
        Assert.True(
            IsValid(
                new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
                {
                    SnapshotDependencies = [new SnapshotDependency( "BuildArtifacts" ) { ArtifactRules = [] }]
                } ) );

        Assert.True(
            IsValid(
                new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
                {
                    ReuseLastSuccessfulBuild = true,
                    SnapshotDependencies = [new SnapshotDependency( "BuildArtifacts" ) { ArtifactRules = ["+:a=>a"] }]
                } ) );
    }

    /// <summary>
    /// A composite waits for its children and downloads nothing from them, so it must never reuse their last
    /// successful build: that would suppress the wait, which is the only thing a composite does.
    /// </summary>
    [Fact]
    public void ACompositeNeverReusesTheLastSuccessfulBuild()
    {
        var composite = new CompositeAdditionalCiBuildConfiguration( "All", "All cells", "BuildArtifacts" ) { ReuseLastSuccessfulBuild = true };

        Assert.True( IsValid( composite ) );
        Assert.Contains( "snapshot(BuildArtifacts)", GenerateCode( composite ), StringComparison.Ordinal );
    }

    [Fact]
    public void ASelfDependencyIsRejected()
        => Assert.False(
            IsValid(
                new PowershellAdditionalCiBuildConfiguration( "Cell", "A test cell", "Build.ps1", "test" )
                {
                    SnapshotDependencies = [new SnapshotDependency( "Cell" )]
                } ) );

    /// <summary>
    /// A cycle makes TeamCity reject the whole settings file, naming neither the product nor the declaration.
    /// </summary>
    [Fact]
    public void ACycleIsRejected()
        => Assert.False(
            IsValid(
                new PowershellAdditionalCiBuildConfiguration( "A", "A", "Build.ps1", "a" ) { SnapshotDependencies = [new SnapshotDependency( "B" )] },
                new PowershellAdditionalCiBuildConfiguration( "B", "B", "Build.ps1", "b" ) { SnapshotDependencies = [new SnapshotDependency( "A" )] } ) );

    [Fact]
    public void ADuplicateIdentifierIsRejected()
        => Assert.False(
            IsValid(
                new PowershellAdditionalCiBuildConfiguration( "BuildArtifacts", "A second one", "Build.ps1", "build" ) ) );

    /// <summary>
    /// A composite aggregates its children, so its edges belong in the graph too, and an unknown identifier there is
    /// the same mistake as anywhere else.
    /// </summary>
    [Fact]
    public void ACompositeParticipatesInValidation()
    {
        Assert.True( IsValid( new CompositeAdditionalCiBuildConfiguration( "All", "All cells", "BuildArtifacts" ) ) );
        Assert.False( IsValid( new CompositeAdditionalCiBuildConfiguration( "All", "All cells", "BuildArtefacts" ) ) );
    }
}
