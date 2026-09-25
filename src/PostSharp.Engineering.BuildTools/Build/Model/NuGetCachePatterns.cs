// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Build.Model;

/// <summary>
/// The NuGet package directories that have to be deleted from the global packages folder before a build restores
/// anything: those produced by the product itself and by the whole closure of its dependencies.
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
    /// <c>New-NuGetCacheCleanupShellCommand</c> in <c>DockerBuild.ps1</c>), so anything else in it would be read by
    /// that shell rather than matched against a directory name. Package identifiers use none of it, so this rejects a
    /// mistake in a product definition instead of constraining a legitimate one.
    /// </summary>
    private static readonly Regex _allowedPattern = new( @"^[a-z0-9._*-]+$", RegexOptions.Compiled );

    /// <summary>
    /// Gets the distinct, ordered set of package directory patterns (the <c>*</c> wildcard is allowed) to delete from
    /// the NuGet global packages folder before each build of the <paramref name="product"/>.
    /// </summary>
    public static string[] GetPatterns( Product product )
    {
        var configurations = new[] { BuildConfiguration.Debug, BuildConfiguration.Release, BuildConfiguration.Public };

        var dependencyPatterns = configurations
            .SelectMany( c => product.DependencyDefinition.GetAllDependencies( c ) )
            .SelectMany( d => d.Definition.PackagePatterns );

        // The packages the product produces itself are included, not just those of its dependencies, so that a stale
        // package from a previous build of this repository cannot leak into the build either.
        var patterns = product.DependencyDefinition.PackagePatterns
            .Concat( dependencyPatterns )

            // NuGet names the directories of the global packages folder in lower case, and the agents match them on a
            // case-sensitive file system, so the comparison is done in the casing the file system has.
            .Select( p => p.ToLowerInvariant() )
            .Distinct( StringComparer.Ordinal )
            .OrderBy( p => p, StringComparer.Ordinal )
            .ToArray();

        Validate( product, patterns );

        return patterns;
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
