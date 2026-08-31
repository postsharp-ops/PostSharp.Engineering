// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System.Linq;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

public class ParametrizedDependencyAliasTests
{
    [Fact]
    public void NoAlias_KeyEqualsName()
    {
        // Use an existing definition to avoid scaffolding ProductFamily registration.
        var definition = MetalamaDependencies.V2026_1.Metalama;
        var dependency = definition.ToDependency();

        Assert.Null( dependency.Alias );
        Assert.Equal( definition.Name, dependency.Key );
        Assert.Equal( definition.NameWithoutDot, dependency.KeyWithoutDot );
        Assert.Equal( DependencyArtifactPickup.Snapshot, dependency.ArtifactPickup );
    }

    [Fact]
    public void WithAlias_KeyAndKeyWithoutDotUseAlias()
    {
        var definition = MetalamaDependencies.V2026_0.Metalama;
        var dependency = definition.WithAlias( "Metalama20260" );

        Assert.Equal( "Metalama20260", dependency.Alias );
        Assert.Equal( "Metalama20260", dependency.Key );
        Assert.Equal( "Metalama20260", dependency.KeyWithoutDot );
        Assert.Equal( definition.Name, dependency.Name ); // Name accessor still surfaces the definition's Name
    }

    [Fact]
    public void WithAlias_StripsDots()
    {
        var definition = MetalamaDependencies.V2026_0.Metalama;
        var dependency = definition.WithAlias( "Foo.Bar.Baz" );

        Assert.Equal( "Foo.Bar.Baz", dependency.Alias );
        Assert.Equal( "Foo.Bar.Baz", dependency.Key );
        Assert.Equal( "FooBarBaz", dependency.KeyWithoutDot );
    }

    [Fact]
    public void WithLastSuccessfulOnly_SetsArtifactPickup()
    {
        var definition = MetalamaDependencies.V2026_0.Metalama;
        var dependency = definition.WithAlias( "Metalama20260" ).WithLastSuccessfulOnly();

        Assert.Equal( DependencyArtifactPickup.LastSuccessful, dependency.ArtifactPickup );
        Assert.Equal( "Metalama20260", dependency.Alias );
    }

    [Fact]
    public void Composes_ConfigurationMappingPlusAliasPlusLastSuccessful()
    {
        var definition = MetalamaDependencies.V2026_0.Metalama;
        var publicMapping = new ConfigurationSpecific<BuildConfiguration>(
            BuildConfiguration.Public,
            BuildConfiguration.Public,
            BuildConfiguration.Public );

        var dependency = definition
            .ToDependency( publicMapping )
            .WithAlias( "Metalama20260" )
            .WithLastSuccessfulOnly();

        Assert.Equal( BuildConfiguration.Public, dependency.ConfigurationMapping[BuildConfiguration.Debug] );
        Assert.Equal( BuildConfiguration.Public, dependency.ConfigurationMapping[BuildConfiguration.Release] );
        Assert.Equal( BuildConfiguration.Public, dependency.ConfigurationMapping[BuildConfiguration.Public] );
        Assert.Equal( "Metalama20260", dependency.Alias );
        Assert.Equal( DependencyArtifactPickup.LastSuccessful, dependency.ArtifactPickup );
    }

    [Fact]
    public void DependencyConfiguration_KeyFallsBackToDefinitionNameWhenNoParametrized()
    {
        var definition = MetalamaDependencies.V2026_1.Metalama;
        var configuration = new DependencyConfiguration( definition, BuildConfiguration.Debug );

        Assert.Null( configuration.Parametrized );
        Assert.Equal( definition.Name, configuration.Key );
        Assert.Equal( definition.NameWithoutDot, configuration.KeyWithoutDot );
        Assert.Equal( DependencyArtifactPickup.Snapshot, configuration.ArtifactPickup );
    }

    [Fact]
    public void DependencyConfiguration_KeyUsesAliasFromParametrized()
    {
        var definition = MetalamaDependencies.V2026_0.Metalama;
        var parametrizedDependency = definition.WithAlias( "Metalama20260" ).WithLastSuccessfulOnly();
        var configuration = new DependencyConfiguration( definition, BuildConfiguration.Public ) { Parametrized = parametrizedDependency };

        Assert.Equal( "Metalama20260", configuration.Key );
        Assert.Equal( "Metalama20260", configuration.KeyWithoutDot );
        Assert.Equal( DependencyArtifactPickup.LastSuccessful, configuration.ArtifactPickup );
    }

    [Fact]
    public void TwoAliasedRefsToSameDefinitionNameAreLookedUpByKeyWithoutThrowing()
    {
        // Reproduces the configuration that triggered the Copilot review's first comment: two ParametrizedDependency
        // entries with the same Definition.Name but different Aliases. Looking them up by Key must succeed unambiguously
        // for each. A naive Name-based SingleOrDefault would throw — the array-level assertion at the end documents
        // why the Product lookup methods only filter on Key.
        var definition = MetalamaDependencies.V2026_0.Metalama;
        var first = definition.WithAlias( "First" );
        var second = definition.WithAlias( "Second" );

        var dependencies = new[] { first, second };

        Assert.Same( first, dependencies.SingleOrDefault( d => d.Key == "First" ) );
        Assert.Same( second, dependencies.SingleOrDefault( d => d.Key == "Second" ) );
        Assert.Null( dependencies.SingleOrDefault( d => d.Key == "Other" ) );

        // Documents the throw that the Name fallback (now removed) would have produced.
        Assert.Throws<System.InvalidOperationException>( () => dependencies.SingleOrDefault( d => d.Name == definition.Name ) );
    }

    [Fact]
    public void DependencyConfiguration_EqualityIgnoresParametrized()
    {
        // (Definition, Configuration) tuple is the equality key. Two configurations with the same Definition+Configuration
        // but different Parametrized references must compare equal so HashSet deduplication in GetAllDependencies stays correct.
        var definition = MetalamaDependencies.V2026_1.Metalama;
        var first = new DependencyConfiguration( definition, BuildConfiguration.Debug );
        var second = new DependencyConfiguration( definition, BuildConfiguration.Debug ) { Parametrized = definition.ToDependency() };

        Assert.Equal( first, second );
        Assert.Equal( first.GetHashCode(), second.GetHashCode() );
    }

    [Fact]
    public void GetAliasForTransitiveDependency_ReturnsNullWhenTheReferenceIsNotAliased()
    {
        var dependency = MetalamaDependencies.V2026_0.Metalama.ToDependency();

        Assert.Null( dependency.GetAliasForTransitiveDependency( MetalamaDependencies.V2026_0.MetalamaCompiler ) );
    }

    [Fact]
    public void GetAliasForTransitiveDependency_AppendsTheDiscriminatorOfTheAlias()
    {
        var dependency = MetalamaDependencies.V2026_0.Metalama.WithAlias( "Metalama20260" );

        Assert.Equal( "Metalama.Compiler_20260", dependency.GetAliasForTransitiveDependency( MetalamaDependencies.V2026_0.MetalamaCompiler ) );
    }

    [Fact]
    public void GetAliasForTransitiveDependency_UsesTheWholeAliasWhenItDoesNotStartWithTheDefinitionName()
    {
        var dependency = MetalamaDependencies.V2026_0.Metalama.WithAlias( "Legacy" );

        Assert.Equal( "Metalama.Compiler_Legacy", dependency.GetAliasForTransitiveDependency( MetalamaDependencies.V2026_0.MetalamaCompiler ) );
    }

    [Fact]
    public void GetAliasForTransitiveDependency_ReturnsNullWhenTheAliasIsTheDefinitionName()
    {
        // Such an alias distinguishes nothing, so there is no discriminator to append.
        var dependency = MetalamaDependencies.V2026_0.Metalama.WithAlias( "Metalama" );

        Assert.Null( dependency.GetAliasForTransitiveDependency( MetalamaDependencies.V2026_0.MetalamaCompiler ) );
    }

    [Fact]
    public void GetAliasForTransitiveDependency_DoesNotRepeatTheUnderscoreAtTheSecondLevel()
    {
        // A key derived by this method is itself the alias of the reference used to reach the next level down. The
        // discriminator must come back as 20260, not _20260, so that the next key gets a single separator.
        var derivedDependency = MetalamaDependencies.V2026_0.MetalamaCompiler.WithAlias( "Metalama.Compiler_20260" );

        Assert.Equal( "Metalama.Premium_20260", derivedDependency.GetAliasForTransitiveDependency( MetalamaDependencies.V2026_0.MetalamaPremium ) );
    }

    [Fact]
    public void KeyWithoutDot_RemovesTheUnderscoreOfTheDiscriminator()
    {
        var dependency = MetalamaDependencies.V2026_0.MetalamaCompiler.WithAlias( "Metalama.Compiler_20260" );

        Assert.Equal( "Metalama.Compiler_20260", dependency.Key );
        Assert.Equal( "MetalamaCompiler20260", dependency.KeyWithoutDot );
    }

    [Fact]
    public void GetAliasForTransitiveDependency_ReturnsNullForAnotherProductFamily()
    {
        // PostSharp.Engineering belongs to the development family, so both versions of Metalama share a single
        // reference to it and it must not inherit the alias.
        var dependency = MetalamaDependencies.V2026_0.Metalama.WithAlias( "Metalama20260" );

        Assert.Null( dependency.GetAliasForTransitiveDependency( DevelopmentDependencies.PostSharpEngineering ) );
    }

    [Fact]
    public void AliasedTransitiveDependencies_IsEmptyWhenNoDirectDependencyIsAliased()
    {
        Assert.Empty( MetalamaDependencies.V2026_1.Metalama.AliasedTransitiveDependencies );
    }

    [Fact]
    public void AliasedTransitiveDependencies_ContainsTheTransitiveDependencyOfTheAliasedDirectDependency()
    {
        // Metalama.Vsx 2026.1 references Metalama 2026.1 directly and Metalama 2026.0 under the Metalama20260 alias.
        var aliasedTransitiveDependencies = MetalamaVsxDependencies.V2026_1.MetalamaVsx.AliasedTransitiveDependencies;

        var compiler = Assert.Contains( "Metalama.Compiler_20260", aliasedTransitiveDependencies );

        Assert.Same( MetalamaDependencies.V2026_0.MetalamaCompiler, compiler.Definition );
        Assert.Equal( "Metalama.Compiler_20260", compiler.Key );
        Assert.Equal( "MetalamaCompiler20260", compiler.KeyWithoutDot );

        // The transitive dependencies of the unaliased direct dependencies, and the dependencies of another family,
        // keep the name of their own definition and are therefore absent from this dictionary.
        Assert.DoesNotContain( "Metalama.Compiler", aliasedTransitiveDependencies.Keys );
        Assert.DoesNotContain( "PostSharp.Engineering_20260", aliasedTransitiveDependencies.Keys );
    }

    [Fact]
    public void GetAllDependencies_GivesTheTwoCompilersTwoDistinctKeys()
    {
        var dependencies = MetalamaVsxDependencies.V2026_1.MetalamaVsx.GetAllDependencies( BuildConfiguration.Debug );

        var currentCompiler = Assert.Single( dependencies, d => ReferenceEquals( d.Definition, MetalamaDependencies.V2026_1.MetalamaCompiler ) );
        var aliasedCompiler = Assert.Single( dependencies, d => ReferenceEquals( d.Definition, MetalamaDependencies.V2026_0.MetalamaCompiler ) );

        Assert.Equal( "Metalama.Compiler", currentCompiler.Key );
        Assert.Equal( "Metalama.Compiler_20260", aliasedCompiler.Key );
        Assert.Equal( "MetalamaCompiler20260", aliasedCompiler.KeyWithoutDot );
    }

    [Fact]
    public void GetAllDependencies_LeavesTheTransitiveDependenciesOfAnUnaliasedDependencyUnchanged()
    {
        // PostSharp.Engineering is a transitive dependency of every Metalama product, so it is reached both through
        // aliased and unaliased paths. It belongs to another product family, so none of its entries is aliased. There
        // is one entry per build configuration it is reached in, which the alias inheritance must not change.
        var dependencies = MetalamaVsxDependencies.V2026_1.MetalamaVsx.GetAllDependencies( BuildConfiguration.Debug );

        var engineeringDependencies = dependencies
            .Where( d => ReferenceEquals( d.Definition, DevelopmentDependencies.PostSharpEngineering ) )
            .ToList();

        Assert.NotEmpty( engineeringDependencies );
        Assert.All( engineeringDependencies, d => Assert.Equal( DevelopmentDependencies.PostSharpEngineering.Name, d.Key ) );
    }

    [Fact]
    public void GetAllDependencies_OfAnUnaliasedProductAssignsNoAlias()
    {
        var dependencies = MetalamaDependencies.V2026_1.Metalama.GetAllDependencies( BuildConfiguration.Debug );

        Assert.All( dependencies, d => Assert.Null( d.Parametrized?.Alias ) );
        Assert.All( dependencies, d => Assert.Equal( d.Definition.Name, d.Key ) );
    }
}
