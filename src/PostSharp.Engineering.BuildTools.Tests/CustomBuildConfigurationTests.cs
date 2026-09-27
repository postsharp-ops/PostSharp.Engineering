// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Triggers;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Xunit;
using MetalamaDependencies = PostSharp.Engineering.BuildTools.Dependencies.Definitions.MetalamaDependencies;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// A product whose public build consumes what an earlier build configuration of the same product produced can replace
/// the standard build configuration with one of its own.
/// </summary>
/// <remarks>
/// What identifies the build configuration to the rest of the product is not the replacement's to choose. Other
/// products address this build by a TeamCity identifier derived from its object name, the deployment reads artifact
/// directories from it by name, and the additional configurations that depend on the build resolve it by that same
/// object name; a replacement that changed any of them would break those silently.
/// </remarks>
public sealed class CustomBuildConfigurationTests
{
    private const string _publishedArtifactRules = "+:artifacts/publish/public/**/*=>artifacts/publish/public";

    private static PowershellAdditionalCiBuildConfiguration CreateReplacement()
        => new( "SignedDistribution", "Build Signed Distribution", "make.ps1", "dist" )
        {
            BuildSnapshotDependency = BuildConfiguration.Public,
            SnapshotDependencies = [new SnapshotDependency( "BuildArtifacts" ) { ArtifactRules = ["+:x-*.7z!**=>"], CleanDestination = false }]
        };

    private static Product CreateProduct( AdditionalCiBuildConfiguration? replacement, params AdditionalCiBuildConfiguration[] additional )
        => new( MetalamaDependencies.V2026_1.Metalama )
        {
            AdditionalCiBuildConfigurations =
            [
                new PowershellAdditionalCiBuildConfiguration( "BuildArtifacts", "Build artifacts", "Build.ps1", "build" ),
                ..additional
            ],
            Configurations = Product.DefaultConfigurations.WithValue(
                BuildConfiguration.Public,
                c => c with { CustomBuildConfiguration = replacement } )
        };

    private static TeamCityBuildConfiguration Generate( Product product )
        => TeamCitySettingsFile.CreateReplacementBuildConfiguration(
            product.Configurations[BuildConfiguration.Public].CustomBuildConfiguration!,
            new ProductProperties( product ),
            new ConfigurationProperties( product, BuildConfiguration.Public ),
            new Dictionary<BuildConfiguration, TeamCityBuildConfiguration>(),
            _publishedArtifactRules,
            ImmutableArray<string>.Empty );

    /// <summary>
    /// The object name is what TeamCity derives the build type identifier from, and other products address this build
    /// by that identifier.
    /// </summary>
    [Fact]
    public void AReplacementKeepsTheIdentityOfTheStandardConfiguration()
    {
        var configuration = Generate( CreateProduct( CreateReplacement() ) );

        Assert.Equal( "PublicBuild", configuration.ObjectName );
        Assert.Equal( "Build [Public]", configuration.Name );
        Assert.Equal( _publishedArtifactRules, configuration.ArtifactRules );
        Assert.True( configuration.RequiresCommitStatusPublisher );
    }

    /// <summary>
    /// The object name a dependency resolves to and the one the build type declares must be the same string, or a
    /// configuration that depends on the build would emit an unresolved Kotlin reference.
    /// </summary>
    [Fact]
    public void ADependencyOnTheBuildResolvesToTheObjectNameItDeclares()
        => Assert.Equal(
            Generate( CreateProduct( CreateReplacement() ) ).ObjectName,
            new SnapshotDependency( BuildConfiguration.Public ).TryGetObjectName( CreateProduct( CreateReplacement() ), [] ) );

    /// <summary>
    /// The replacement keeps its own dependencies on other build configurations of the same product.
    /// </summary>
    [Fact]
    public void AReplacementKeepsItsOwnDependencies()
    {
        var configuration = Generate( CreateProduct( CreateReplacement() ) );

        Assert.Contains(
            configuration.SnapshotDependencies!,
            d => string.Equals( d.ObjectId, "BuildArtifacts", StringComparison.Ordinal ) && !d.IsAbsoluteId );
    }

    /// <summary>
    /// The factory that materialises the replacement appends the dependencies on other products itself, keyed on the
    /// artifact layout it reads, while the product's own graph is keyed on this build configuration. Emitting both
    /// would name the same build type twice, under artifact rules that need not even agree.
    /// </summary>
    [Fact]
    public void EachBuildTypeIsDependedOnOnce()
    {
        // Metalama.Premium, rather than the product the other tests use, because it declares dependencies on other
        // products. Without them the replacement's factory would add nothing to duplicate and this would pass for
        // the wrong reason, so the count is asserted to be non-zero first.
        var product = new Product( MetalamaDependencies.V2026_1.MetalamaPremium )
        {
            AdditionalCiBuildConfigurations = [new PowershellAdditionalCiBuildConfiguration( "BuildArtifacts", "Build artifacts", "Build.ps1", "build" )],
            Configurations = Product.DefaultConfigurations.WithValue(
                BuildConfiguration.Public,
                c => c with { CustomBuildConfiguration = CreateReplacement() } )
        };

        var crossProductDependencies = new ConfigurationProperties( product, BuildConfiguration.Public ).SnapshotDependenciesForBuildConfiguration;

        Assert.NotEmpty( crossProductDependencies );

        var configuration = TeamCitySettingsFile.CreateReplacementBuildConfiguration(
            product.Configurations[BuildConfiguration.Public].CustomBuildConfiguration!,
            new ProductProperties( product ),
            new ConfigurationProperties( product, BuildConfiguration.Public ),
            new Dictionary<BuildConfiguration, TeamCityBuildConfiguration>(),
            _publishedArtifactRules,
            ImmutableArray<string>.Empty );

        var objectIds = configuration.SnapshotDependencies!.Select( d => d.ObjectId ).ToList();

        Assert.Equal( objectIds.Count, objectIds.Distinct( StringComparer.Ordinal ).Count() );

        // The dependencies on other products are the product's, not the replacement's.
        Assert.Equal( crossProductDependencies.Length, configuration.SnapshotDependencies!.Count( d => d.IsAbsoluteId ) );
    }

    /// <summary>
    /// The additional-configuration factory requests an SSH agent unconditionally, whereas the standard build ties it
    /// to the upstream check. A replacement that runs no upstream check has no use for the key.
    /// </summary>
    [Fact]
    public void AReplacementThatRunsNoUpstreamCheckRequestsNoSshAgent()
        => Assert.False( Generate( CreateProduct( CreateReplacement() ) ).IsSshAgentRequired );

    /// <summary>
    /// The declared flag is not the condition. The default public configuration sets it, so a validation that tested
    /// the flag alone would reject every product that uses that record unchanged, which is all of them.
    /// </summary>
    [Fact]
    public void TheUpstreamCheckIsInertWhereTheProductHasAReleaseBranch()
    {
        var product = CreateProduct( CreateReplacement() );

        Assert.True( Product.DefaultConfigurations.Public.RequiresUpstreamCheck );
        Assert.NotNull( product.DependencyDefinition.ReleaseBranch );
        Assert.False( TeamCitySettingsFile.RequiresUpstreamCheck( product, product.Configurations[BuildConfiguration.Public] ) );
    }

    [Fact]
    public void TheDefaultPublicConfigurationIsAcceptedAsAReplacementHost()
        => Assert.True( SnapshotDependencyGraph.TryValidate( new ConsoleHelper(), CreateProduct( CreateReplacement() ) ) );

    [Fact]
    public void ArtifactRulesOnTheReplacementAreRejected()
    {
        var replacement = new PowershellAdditionalCiBuildConfiguration( "SignedDistribution", "Signed", "make.ps1", "dist" )
        {
            ArtifactRules = ["+:something=>somewhere"]
        };

        Assert.False( SnapshotDependencyGraph.TryValidate( new ConsoleHelper(), CreateProduct( replacement ) ) );
    }

    [Fact]
    public void BuildTriggersOnTheReplacementAreRejected()
    {
        var replacement = new PowershellAdditionalCiBuildConfiguration( "SignedDistribution", "Signed", "make.ps1", "dist" )
        {
            BuildTriggers = [new SourceBuildTrigger()]
        };

        Assert.False( SnapshotDependencyGraph.TryValidate( new ConsoleHelper(), CreateProduct( replacement ) ) );
    }

    /// <summary>
    /// The replacement becomes the build configuration's build type. Listing it among the additional configurations
    /// as well would generate the same Kotlin object twice.
    /// </summary>
    [Fact]
    public void AReplacementAlsoListedAsAnAdditionalConfigurationIsRejected()
    {
        var replacement = CreateReplacement();

        Assert.False( SnapshotDependencyGraph.TryValidate( new ConsoleHelper(), CreateProduct( replacement, replacement ) ) );
    }

    /// <summary>
    /// The loop over the additional configurations assigns the GitHub App identity after the factory returns, and a
    /// replacement never goes through that loop. Without carrying it, the build would run under the repository's
    /// default identity instead of the one the product configured, which nothing in the generated file would show.
    /// </summary>
    [Fact]
    public void AReplacementKeepsItsGitHubAppTokenOverride()
    {
        var replacement = new PowershellAdditionalCiBuildConfiguration( "SignedDistribution", "Signed", "make.ps1", "dist" )
        {
            GitHubAppToken = new GitHubAppTokenOverride( "SomeConnectionId" )
        };

        Assert.Equal( "SomeConnectionId", Generate( CreateProduct( replacement ) ).GitHubAppTokenOverride?.ConnectionId );
    }

    /// <summary>
    /// Declaring no replacement leaves everything as it was, which is the case of every product that exists today.
    /// </summary>
    [Fact]
    public void AProductWithoutAReplacementIsUnaffected()
    {
        var product = CreateProduct( null );

        Assert.Null( product.Configurations[BuildConfiguration.Public].CustomBuildConfiguration );
        Assert.True( SnapshotDependencyGraph.TryValidate( new ConsoleHelper(), product ) );
    }

    /// <summary>
    /// The whole settings file of a product that declares no replacement still holds the standard build configuration,
    /// running the standard build step. This is the regression guard for every product that exists today, and it
    /// exercises the generator end to end, including the validation that now runs before anything is written.
    /// </summary>
    [Fact]
    public void AProductWithoutAReplacementStillGeneratesTheStandardBuildStep()
    {
        MSBuildHelper.InitializeLocator();

        using var directory = new TempDirectory();

        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            GenerateDockerfiles = false,
            OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows )
        };

        Assert.True( GenerateScriptsCommand.Execute( TestBuildContext.Create( directory.Path, product ), new CommonCommandSettings() ) );

        var settings = File.ReadAllText( Path.Combine( directory.Path, ".teamcity", "settings.kts" ) );

        Assert.Contains( "object PublicBuild : BuildType({", settings, StringComparison.Ordinal );
        Assert.Contains( "test --configuration Public", settings, StringComparison.Ordinal );
    }
}
