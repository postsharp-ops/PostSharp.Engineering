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
}
