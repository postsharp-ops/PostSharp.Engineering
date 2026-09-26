// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// What has to be in the list of package directories deleted before a build restores anything: the product itself, the
/// whole closure of its package dependencies, and its source dependencies with their closures.
/// </summary>
/// <remarks>
/// A pattern left out is a package that the build may restore from the cache of the agent while its artifacts are being
/// rebuilt, and every continuous-integration build of a product carries the same public version, so the cache is never
/// invalidated by the version. Whatever is missing here is silently tested against a package of an earlier build.
/// </remarks>
public sealed class NuGetCachePatternsTests
{
    private static string[] GetPatterns( DependencyDefinition definition ) => NuGetCachePatterns.GetPatterns( new Product( definition ) );

    /// <summary>
    /// A stale package of the repository being built leaks into its own build exactly as a dependency's does, so the
    /// product's own packages are deleted too.
    /// </summary>
    [Theory]
    [MemberData( nameof(Products) )]
    public void ThePackagesOfTheProductItselfAreIncluded( string name, DependencyDefinition definition )
    {
        _ = name;
        var patterns = GetPatterns( definition );

        foreach ( var own in definition.PackagePatterns )
        {
            Assert.Contains( own.ToLowerInvariant(), patterns, StringComparer.Ordinal );
        }
    }

    /// <summary>
    /// The closure, not the direct dependencies. A package two edges away is restored by the build like any other, and
    /// the products of these families are layered deeply enough that the difference is most of the list.
    /// </summary>
    [Theory]
    [MemberData( nameof(Products) )]
    public void TheWholePackageDependencyClosureIsIncluded( string name, DependencyDefinition definition )
    {
        _ = name;
        var patterns = GetPatterns( definition );

        foreach ( var direct in definition.Dependencies )
        {
            // Two edges out, which no set of direct dependencies can account for.
            foreach ( var transitive in direct.Definition.Dependencies )
            {
                foreach ( var pattern in transitive.Definition.PackagePatterns )
                {
                    Assert.Contains( pattern.ToLowerInvariant(), patterns, StringComparer.Ordinal );
                }
            }
        }
    }

    /// <summary>
    /// A source dependency is built from source by the consuming build and produces packages of its own, so a stale copy
    /// of them is in the cache for the same reason and has to go the same way. This is what the list used to miss: it
    /// was computed from the package dependencies alone.
    /// </summary>
    [Theory]
    [MemberData( nameof(Products) )]
    public void TheSourceDependenciesAndTheirClosuresAreIncluded( string name, DependencyDefinition definition )
    {
        _ = name;
        var patterns = GetPatterns( definition );

        foreach ( var sourceDependency in GetSourceDependencyClosure( definition ) )
        {
            foreach ( var pattern in sourceDependency.PackagePatterns )
            {
                Assert.Contains( pattern.ToLowerInvariant(), patterns, StringComparer.Ordinal );
            }

            // And what that source dependency itself depends on, since building it restores those.
            foreach ( var dependency in sourceDependency.Dependencies )
            {
                foreach ( var pattern in dependency.Definition.PackagePatterns )
                {
                    Assert.Contains( pattern.ToLowerInvariant(), patterns, StringComparer.Ordinal );
                }
            }
        }
    }

    /// <summary>
    /// The case measured on the real definitions, kept as a regression: <c>NopCommerce</c> is a source dependency of the
    /// consolidated Metalama product and not a package dependency of anything, so its two patterns were absent from the
    /// list of every build of that product.
    /// </summary>
    [Fact]
    public void ASourceDependencyThatIsNotAPackageDependencyIsIncluded()
    {
        var definition = MetalamaDependencies.V2026_1.Consolidated;

        Assert.DoesNotContain( definition.Dependencies, d => d.Definition == MetalamaDependencies.V2026_1.NopCommerce );
        Assert.Contains( MetalamaDependencies.V2026_1.NopCommerce, definition.SourceDependencies );

        var patterns = GetPatterns( definition );

        Assert.Contains( "metalama.tests.nopcommerce", patterns, StringComparer.Ordinal );
        Assert.Contains( "metalama.tests.nopcommerce.*", patterns, StringComparer.Ordinal );
    }

    /// <summary>
    /// The order is a function of the set alone, not of the order the dependency graph is walked in. The list is baked
    /// into two committed generated files, so an order that moved would put a spurious diff in every repository on every
    /// regeneration and hide the real change in it.
    /// </summary>
    [Theory]
    [MemberData( nameof(Products) )]
    public void ThePatternsAreDistinctAndDeterministicallyOrdered( string name, DependencyDefinition definition )
    {
        _ = name;
        var patterns = GetPatterns( definition );

        Assert.NotEmpty( patterns );
        Assert.Equal( patterns.Distinct( StringComparer.Ordinal ).Count(), patterns.Length );

        // Ordinal, so that the sequence does not depend on the culture of the machine running generate-scripts either.
        Assert.Equal( patterns.OrderBy( p => p, StringComparer.Ordinal ), patterns );

        // Nothing carried over from the traversal: a second call gives the same sequence, not merely the same set. The
        // traversal goes through a HashSet, whose enumeration order is not part of its contract.
        Assert.Equal( patterns, GetPatterns( definition ) );

        // The directories of the global packages folder are lower case, and a Unix agent matches them as they are.
        Assert.All( patterns, p => Assert.Equal( p.ToLowerInvariant(), p ) );
    }

    private static IEnumerable<DependencyDefinition> GetSourceDependencyClosure( DependencyDefinition definition )
    {
        var visited = new HashSet<DependencyDefinition>();
        var queue = new Queue<DependencyDefinition>( definition.SourceDependencies );

        while ( queue.Count > 0 )
        {
            var current = queue.Dequeue();

            if ( !visited.Add( current ) )
            {
                continue;
            }

            yield return current;

            foreach ( var sourceDependency in current.SourceDependencies )
            {
                queue.Enqueue( sourceDependency );
            }
        }
    }

    /// <summary>
    /// A consolidated product and a plain one of each family: the consolidated ones are where source dependencies are
    /// declared, and the plain ones are what the failing builds were.
    /// </summary>
    public static TheoryData<string, DependencyDefinition> Products
        => new()
        {
            { "PostSharp 2027.0 Consolidated", PostSharpDependencies.V2027_0.Consolidated },
            { "PostSharp 2027.0", PostSharpDependencies.V2027_0.PostSharp },
            { "Metalama 2026.1 Consolidated", MetalamaDependencies.V2026_1.Consolidated },
            { "Metalama 2026.1", MetalamaDependencies.V2026_1.Metalama },
            { "PostSharp.Engineering", DevelopmentDependencies.PostSharpEngineering }
        };
}
