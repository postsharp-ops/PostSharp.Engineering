// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Build.Model;

/// <summary>
/// The NuGet package directories that have to be deleted from the global packages folder before a build restores
/// anything: those produced by the product itself, by the whole closure of its package dependencies, and by its source
/// dependencies with their own closures.
/// </summary>
/// <remarks>
/// <para>
/// Every continuous-integration build of a product carries the same public package version, and NuGet never extracts
/// a version that is already in the global packages folder. Without this deletion a build restores whatever copy an
/// earlier build left there instead of the artifacts it depends on, and a test that passes proves nothing about the
/// code under test.
/// </para>
/// <para>
/// Two generators need the same list, which is why it lives here rather than in either of them: the TeamCity build
/// configuration, whose first step deletes what the agent itself can delete, and <c>DockerBuild.ps1</c>, which bakes
/// the list in and deletes from inside the container. Both have to name exactly the same directories -- a list that
/// differed between them would leave the difference behind on the agents where only the container can delete.
/// </para>
/// </remarks>
internal static class NuGetCachePatterns
{
    /// <summary>
    /// The characters a pattern may contain. The value reaches a POSIX shell as an unquoted glob (see
    /// <c>New-TestCommandPrefix</c> in <c>Resources/CleanUpBuildAgent.ps1</c>), so anything else in it would be read by
    /// that shell rather than matched against a directory name. Package identifiers use none of it, so this rejects a
    /// mistake in a product definition instead of constraining a legitimate one.
    /// </summary>
    private static readonly Regex _allowedPattern = new( @"^[a-z0-9._*-]+$", RegexOptions.Compiled );

    /// <summary>
    /// Gets the distinct, ordered set of package directory patterns (the <c>*</c> wildcard is allowed) to delete from
    /// the NuGet global packages folder before each build of the <paramref name="product"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything the build can reach, over both kinds of edge and transitively: the product itself, the whole closure
    /// of its package dependencies, and its source dependencies with their closures. Anything left out is a package
    /// the build may restore from the cache while its artifacts are being rebuilt, which is the whole defect.
    /// </para>
    /// <para>
    /// A source dependency is built from source by the consuming build and produces packages of its own, so leaving it
    /// out was a real hole rather than a theoretical one: in the consolidated PostSharp 2027.0 product, Backstage is
    /// declared only as a source dependency, and the stale package that started this was a Backstage one.
    /// </para>
    /// <para>
    /// The traversal is its own rather than <see cref="DependencyDefinition.GetAllDependencies"/>, which walks package
    /// dependencies alone. The build configuration does not enter into it either: it decides which artifacts of a
    /// dependency are consumed, not which products are reachable, and every configuration of every reachable product
    /// leaves its packages under the same directory of the cache.
    /// </para>
    /// </remarks>
    public static string[] GetPatterns( Product product )
    {
        // Reference identity is enough to terminate on a cycle or a diamond, the definitions being singletons; two
        // instances describing one product would be visited twice and their patterns deduplicated below anyway.
        var visited = new HashSet<DependencyDefinition>();
        var collected = new List<string>();

        CollectRecursive( product.DependencyDefinition );

        var patterns = collected

            // NuGet names the directories of the global packages folder in lower case, and the agents match them on a
            // case-sensitive file system, so the comparison is done in the casing the file system has.
            .Select( p => p.ToLowerInvariant() )
            .Distinct( StringComparer.Ordinal )

            // Sorted last, and by ordinal, so that the order is a function of the set alone: neither the order the graph
            // happens to be walked in nor the culture of the machine running generate-scripts may enter into it. The
            // list is baked into two generated and committed files, DockerBuild.ps1 and .teamcity/settings.kts, so an
            // order that moved would show up as a diff in every repository on every regeneration, and the real change
            // hidden in it would be unreviewable.
            .OrderBy( p => p, StringComparer.Ordinal )
            .ToArray();

        Validate( product, patterns );

        return patterns;

        void CollectRecursive( DependencyDefinition definition )
        {
            if ( !visited.Add( definition ) )
            {
                return;
            }

            collected.AddRange( definition.PackagePatterns );

            foreach ( var dependency in definition.Dependencies )
            {
                CollectRecursive( dependency.Definition );
            }

            foreach ( var sourceDependency in definition.SourceDependencies )
            {
                CollectRecursive( sourceDependency );
            }
        }
    }

    private static void Validate( Product product, IEnumerable<string> patterns )
    {
        var invalid = patterns.Where( p => !_allowedPattern.IsMatch( p ) ).ToArray();

        if ( invalid.Length > 0 )
        {
            throw new InvalidOperationException(
                $"The package patterns of '{product.ProductName}' or of one of its dependencies contain characters "
                + "that cannot be used to clean the NuGet cache, because the pattern is passed to the shell of a "
                + $"container as a glob: {string.Join( ", ", invalid )}. Allowed are letters, digits, '.', '_', '-' "
                + "and the '*' wildcard." );
        }
    }
}
