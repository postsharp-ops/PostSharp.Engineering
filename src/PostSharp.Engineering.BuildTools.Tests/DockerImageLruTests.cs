// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The image-space cleanup in <c>DockerBuild.ps1</c> used to evict the oldest image, because the Docker engine exposes
/// no last-used time of its own. Age is a poor proxy for value: a stable base image is built once and then reused by
/// every build for months, so it is simultaneously the oldest image on the agent and the most expensive one to lose.
/// The script therefore records each use itself, and evicts least-recently-used first.
///
/// These tests run the real <c>Get-EvictionCandidates</c>, lifted verbatim out of the shipped script, against a
/// fabricated usage directory and a stubbed <c>docker</c> command, so no Docker engine is needed.
/// </summary>
public class DockerImageLruTests
{
    // Distinct, readable stand-ins for the 64-hex image identifiers that `docker image ls --no-trunc` prints.
    private const string _oldButHot = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string _youngButCold = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string _mirrored = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    /// <summary>
    /// One row of <c>docker image ls --no-trunc --format '{{.ID}}|{{.Repository}}|{{.Tag}}|{{.CreatedAt}}|{{.Size}}'</c>.
    /// The creation date is written in the Go layout Docker really prints, of which the script reads the leading
    /// timestamp only.
    /// </summary>
    private static string Row( string id, string repository, string tag, DateTime created, string size )
        => FormattableString.Invariant(
            $"sha256:{id}|{repository}|{tag}|{created:yyyy-MM-dd HH:mm:ss} +0200 CEST|{size}" );

    /// <summary>
    /// Runs <c>Get-EvictionCandidates</c> over the given image rows and usage records, and returns the identifiers in
    /// the order the function selected them — which is the order in which they would be removed.
    /// </summary>
    /// <param name="usage">Image identifier to the content of its usage record, or <c>null</c> to write no record.</param>
    private static IReadOnlyList<string> SelectCandidates(
        string executable,
        IReadOnlyList<string> rows,
        IReadOnlyDictionary<string, string?> usage )
    {
        var usageDirectory = Path.Combine( Path.GetTempPath(), $"dockerbuild-lru-{Guid.NewGuid():N}" );
        Directory.CreateDirectory( usageDirectory );

        try
        {
            foreach ( var (id, content) in usage )
            {
                if ( content != null )
                {
                    File.WriteAllText( Path.Combine( usageDirectory, id ), content, new UTF8Encoding( false ) );
                }
            }

            var rowLiterals = string.Join( ", ", rows.Select( r => $"'{DockerBuildScript.Escape( r )}'" ) );

            // `docker` is shadowed by a function: PowerShell resolves a function before a native command, so the real
            // engine is never contacted. $LASTEXITCODE is assigned explicitly because only native commands set it.
            var script = $$"""
                          $ErrorActionPreference = 'Stop'
                          $global:LASTEXITCODE = 0

                          function docker
                          {
                              $joined = $args -join ' '
                              if ( $joined -like 'image ls*' ) { $global:LASTEXITCODE = 0; return @({{rowLiterals}}) }
                              if ( $joined -like 'ps *' ) { $global:LASTEXITCODE = 0; return @() }
                              throw "The test stub was asked for an unexpected docker command: $joined"
                          }

                          function Get-ImageUsageDirectory
                          {
                              return '{{DockerBuildScript.Escape( usageDirectory )}}'
                          }

                          {{DockerBuildScript.ExtractFunction( "ConvertFrom-DockerSize" )}}

                          {{DockerBuildScript.ExtractFunction( "Get-ImageUsageRecords" )}}

                          {{DockerBuildScript.ExtractFunction( "Get-EvictionCandidates" )}}

                          $keep = [System.Collections.Generic.HashSet[string]]::new( [StringComparer]::OrdinalIgnoreCase )
                          $candidates = Get-EvictionCandidates $keep (Get-Date).AddHours(-2)

                          foreach ( $candidate in $candidates )
                          {
                              Write-Output "CANDIDATE $( $candidate.Id ) $( $candidate.References -join ',' )"
                          }
                          """;

            var output = DockerBuildScript.Run( executable, script );

            return output
                .Split( '\n' )
                .Select( line => line.Trim() )
                .Where( line => line.StartsWith( "CANDIDATE ", StringComparison.Ordinal ) )
                .Select( line => line["CANDIDATE ".Length..] )
                .ToList();
        }
        finally
        {
            Directory.Delete( usageDirectory, true );
        }
    }

    private static string UsageRecord( DateTime lastUsedUtc, params string[] references )
        => string.Join( "\n", new[] { lastUsedUtc.ToUniversalTime().ToString( "o", CultureInfo.InvariantCulture ) }.Concat( references ) );

    /// <summary>
    /// The regression this whole change exists for. The old image is the one every build uses; the young one has sat
    /// untouched since it was built. Sorting by creation date evicts exactly the wrong one.
    /// </summary>
    [Fact]
    public void AnOldImageUsedRecently_IsEvictedAfterAYoungerImageThatWasNotUsed()
    {
        var executable = DockerBuildScript.FindPowerShell();

        if ( executable == null )
        {
            return;
        }

        var now = DateTime.Now;

        var candidates = SelectCandidates(
            executable,
            [
                Row( _oldButHot, "postsharp-build", "hot", now.AddDays( -100 ), "40GB" ),
                Row( _youngButCold, "postsharp-leaf", "cold", now.AddDays( -2 ), "10GB" )
            ],
            new Dictionary<string, string?>
            {
                // Built a hundred days ago, but used yesterday.
                [_oldButHot] = UsageRecord( now.AddDays( -1 ), "postsharp-build:hot" )
            } );

        Assert.Equal( 2, candidates.Count );

        Assert.StartsWith( _youngButCold, candidates[0], StringComparison.Ordinal );
        Assert.StartsWith( _oldButHot, candidates[1], StringComparison.Ordinal );
    }

    /// <summary>
    /// What makes the change safe to deploy: every image already on an agent is unrecorded, so until builds start
    /// recording their images the order has to be exactly the one the cleanup produced before.
    /// </summary>
    [Fact]
    public void WithNoUsageRecords_TheOrderFallsBackToTheCreationDate()
    {
        var executable = DockerBuildScript.FindPowerShell();

        if ( executable == null )
        {
            return;
        }

        var now = DateTime.Now;

        var candidates = SelectCandidates(
            executable,
            [
                Row( _youngButCold, "postsharp-leaf", "young", now.AddDays( -2 ), "10GB" ),
                Row( _oldButHot, "postsharp-build", "old", now.AddDays( -100 ), "40GB" )
            ],
            new Dictionary<string, string?>() );

        Assert.Equal( 2, candidates.Count );

        Assert.StartsWith( _oldButHot, candidates[0], StringComparison.Ordinal );
        Assert.StartsWith( _youngButCold, candidates[1], StringComparison.Ordinal );
    }

    /// <summary>
    /// An OS image mirrored into the registry keeps its upstream name as well, so one image carries two unrelated
    /// references. It must stay one candidate with one last-used time — which is why the records are keyed by image
    /// identifier and not by reference.
    /// </summary>
    [Fact]
    public void AnImageWithSeveralReferences_YieldsOneCandidateCarryingBoth()
    {
        var executable = DockerBuildScript.FindPowerShell();

        if ( executable == null )
        {
            return;
        }

        var now = DateTime.Now;

        var candidates = SelectCandidates(
            executable,
            [
                Row( _mirrored, "registry.home/windows-servercore", "ltsc2025", now.AddDays( -30 ), "6GB" ),
                Row( _mirrored, "mcr.microsoft.com/windows/servercore", "ltsc2025", now.AddDays( -30 ), "6GB" )
            ],
            new Dictionary<string, string?>
            {
                [_mirrored] = UsageRecord( now.AddDays( -3 ), "registry.home/windows-servercore:ltsc2025" )
            } );

        var candidate = Assert.Single( candidates );

        Assert.Contains( "registry.home/windows-servercore:ltsc2025", candidate, StringComparison.Ordinal );
        Assert.Contains( "mcr.microsoft.com/windows/servercore:ltsc2025", candidate, StringComparison.Ordinal );
    }

    /// <summary>
    /// The grace window has to cover the recorded use as well as the creation date, or an old image that a concurrent
    /// run on the same agent is using right now is a prime eviction candidate.
    /// </summary>
    [Fact]
    public void AnOldImageUsedInsideTheGraceWindow_IsNotACandidate()
    {
        var executable = DockerBuildScript.FindPowerShell();

        if ( executable == null )
        {
            return;
        }

        var now = DateTime.Now;

        var candidates = SelectCandidates(
            executable,
            [
                Row( _oldButHot, "postsharp-build", "in-use", now.AddDays( -100 ), "40GB" ),
                Row( _youngButCold, "postsharp-leaf", "cold", now.AddDays( -2 ), "10GB" )
            ],
            new Dictionary<string, string?>
            {
                // A sibling run touched it a minute ago.
                [_oldButHot] = UsageRecord( now.AddMinutes( -1 ), "postsharp-build:in-use" )
            } );

        var candidate = Assert.Single( candidates );

        Assert.StartsWith( _youngButCold, candidate, StringComparison.Ordinal );
    }

    /// <summary>
    /// A truncated or corrupt record must cost the signal for one image, not throw and take the build's cleanup down
    /// with it.
    /// </summary>
    [Fact]
    public void ACorruptUsageRecord_IsIgnoredRatherThanThrowing()
    {
        var executable = DockerBuildScript.FindPowerShell();

        if ( executable == null )
        {
            return;
        }

        var now = DateTime.Now;

        var candidates = SelectCandidates(
            executable,
            [Row( _oldButHot, "postsharp-build", "old", now.AddDays( -100 ), "40GB" )],
            new Dictionary<string, string?> { [_oldButHot] = "this is not a timestamp" } );

        // It falls back to the file's modification time, which the test has just written, so the image is inside the
        // grace window and is not offered for eviction. The point is that the run completed at all.
        Assert.Empty( candidates );
    }

    /// <summary>
    /// Once over budget the cleanup frees down to a fraction of the budget rather than to just under the line.
    /// Without the gap, a store resting a little above the budget runs a cleanup on every single build and frees
    /// almost nothing each time.
    /// </summary>
    [Fact]
    public void TheCleanupFreesDownToSeventyPercentOfTheBudget()
    {
        var script = DockerBuildScript.Text;

        var match = Regex.Match( script, @"\$ImageCleanupTargetFraction\s*=\s*(?<value>[\d.]+)" );
        Assert.True( match.Success, "DockerBuild.ps1 no longer defines $ImageCleanupTargetFraction." );

        var fraction = double.Parse( match.Groups["value"].Value, CultureInfo.InvariantCulture );
        Assert.Equal( 0.70, fraction, 3 );

        // A budget of 100 decimal GB leaves a 70 GB target, so at 101 GB the cleanup aims to free 31 GB.
        const long budget = 100_000_000_000L;
        var target = (long) (budget * fraction);
        Assert.Equal( 70_000_000_000L, target );
        Assert.Equal( 31_000_000_000L, 101_000_000_000L - target );

        Assert.Contains( "$targetBytes = [long]($budgetBytes * $ImageCleanupTargetFraction)", script, StringComparison.Ordinal );

        // The trigger stays the budget, but everything that decides how much to remove uses the target.
        Assert.Contains( "-and $total -gt $targetBytes; $pass++", script, StringComparison.Ordinal );
        Assert.Contains( "$overage = $total - $targetBytes", script, StringComparison.Ordinal );
    }

    /// <summary>
    /// The base OS image has to be recorded as used like any other: on a Windows agent it is both the largest and
    /// the oldest image on the machine, so an eviction order that never sees it in use removes it first and the next
    /// build re-pulls tens of gigabytes.
    ///
    /// Asserted on the script rather than end to end, because it takes a product whose chain root declares
    /// <c>ARG OS_IMAGE_REPOSITORY</c> for <c>Get-OsImageSpec</c> to resolve an OS image at all. The lightweight
    /// fixtures in <c>tests/dockerbuild</c> deliberately declare none, which is why the matching case there is
    /// skipped.
    /// </summary>
    [Fact]
    public void TheBaseOsImage_IsRecordedAsUsed()
    {
        var script = DockerBuildScript.Text;

        // Recording is driven by the keep set, and happens once the chain is resolved.
        Assert.Contains( "Touch-ImageUsage (Get-ImageChainKeepSet)", script, StringComparison.Ordinal );

        // The keep set carries the OS image of the chain root, and its mirror when a registry is configured.
        var keepSet = DockerBuildScript.ExtractFunction( "Get-ImageChainKeepSet" );

        Assert.Contains( "$osImageSpec = Get-OsImageSpec $root", keepSet, StringComparison.Ordinal );
        Assert.Contains( "Get-OsMirrorRepository $osImageSpec.Repository", keepSet, StringComparison.Ordinal );
    }

    /// <summary>
    /// The batch is selected least-recently-used first but must be REMOVED newest first, because Docker refuses to
    /// remove an image that a descendant is built on and the descendant is always the younger image. Guards against
    /// someone "fixing" the apparent inconsistency by sorting that loop by <c>LastUsed</c> too.
    /// </summary>
    [Fact]
    public void TheRemovalLoop_StillOrdersByCreationDate()
    {
        var script = DockerBuildScript.Text;

        Assert.Contains( "$batch | Sort-Object Created -Descending", script, StringComparison.Ordinal );
        Assert.Contains( "Sort-Object LastUsed)", script, StringComparison.Ordinal );
    }
}
