// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PostSharp.Engineering.BuildTools.Build.Model;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The Claude image bakes a weekly cache-buster through <c>COPY .g/update.timestamp</c>, so the file has to sit in the
/// build context of the image that declares that COPY — <c>docker-context/&lt;stem&gt;/.g/</c>. The stem carries a
/// product-defined prefix (<c>AdditionalDockerfile( "agent", ... )</c> yields <c>agent-claude.Dockerfile</c>), so a
/// destination hardcoded to <c>docker-context/claude</c> leaves every prefixed leaf without the file and the build
/// fails on the COPY. That failure only surfaces after several minutes of image building, which is why the path
/// computation is pinned here instead.
/// </summary>
public class DockerTimestampContextTests
{
    /// <summary>
    /// Reads the script from the assembly rather than from the working tree. This is the copy that
    /// <c>generate-scripts</c> extracts into every consuming repository, so it is the artifact whose behaviour
    /// matters, and it is reachable regardless of where the test host runs from.
    /// </summary>
    private static string ScriptText
    {
        get
        {
            const string resourceName = "PostSharp.Engineering.BuildTools.Resources.DockerBuild.ps1";

            using var stream = typeof(Product).Assembly.GetManifestResourceStream( resourceName )
                               ?? throw new InvalidOperationException( $"Cannot find the embedded resource '{resourceName}'." );

            using var reader = new StreamReader( stream );

            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// Extracts a whole <c>function</c> block from the script. The functions are indented by four spaces and closed by
    /// a brace at the same indentation, so the block runs to the first such brace.
    /// </summary>
    private static string ExtractFunction( string name )
    {
        var match = Regex.Match(
            ScriptText,
            @"^    function\s+" + Regex.Escape( name ) + @"\b.*?^    \}",
            RegexOptions.Multiline | RegexOptions.Singleline );

        Assert.True( match.Success, $"Could not extract the '{name}' function from DockerBuild.ps1." );

        // Strip the leading indentation so the extracted text is valid at the top level of a script.
        return Regex.Replace( match.Value, "^    ", "", RegexOptions.Multiline );
    }

    private static string? FindPowerShell()
    {
        foreach ( var executable in new[] { "pwsh", "powershell" } )
        {
            try
            {
                using var process = Process.Start(
                    new ProcessStartInfo( executable, "-NoProfile -Command \"exit 0\"" )
                    {
                        RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
                    } );

                if ( process == null )
                {
                    continue;
                }

                process.WaitForExit( 30_000 );

                if ( process.ExitCode == 0 )
                {
                    return executable;
                }
            }
            catch ( Exception )
            {
                // Not on this machine; try the next one.
            }
        }

        return null;
    }

    private static string RunPowerShell( string executable, string script )
    {
        var scriptFile = Path.Combine( Path.GetTempPath(), $"dockerbuild-test-{Guid.NewGuid():N}.ps1" );
        File.WriteAllText( scriptFile, script, new UTF8Encoding( false ) );

        try
        {
            using var process = Process.Start(
                new ProcessStartInfo( executable, $"-NoProfile -NonInteractive -File \"{scriptFile}\"" )
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
                } )!;

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit( 120_000 );

            Assert.True( process.ExitCode == 0, $"PowerShell exited with {process.ExitCode}:\n{output}" );

            return output;
        }
        finally
        {
            File.Delete( scriptFile );
        }
    }

    /// <summary>
    /// Runs the real <c>Copy-TimestampToContext</c> (and the helpers it depends on), lifted verbatim out of the shipped
    /// script, against a scratch context directory. Returns the directory so the caller can assert on the layout.
    /// </summary>
    private static string StageTimestamp( string executable, IReadOnlyList<string> dockerfiles )
    {
        var root = Path.Combine( Path.GetTempPath(), $"dockerbuild-ctx-{Guid.NewGuid():N}" );
        var contextDirectory = Path.Combine( root, "docker-context" );
        var dockerfileDirectory = Path.Combine( root, "docker" );
        Directory.CreateDirectory( contextDirectory );
        Directory.CreateDirectory( dockerfileDirectory );

        // A stand-in for the file Get-TimestampFile would have produced in LOCALAPPDATA.
        var sourceTimestamp = Path.Combine( root, "update.timestamp" );
        File.WriteAllText( sourceTimestamp, "2026-07-13" );

        var invocations = new StringBuilder();

        foreach ( var dockerfile in dockerfiles )
        {
            // Only a Claude leaf bakes the cache-buster; the others must be left alone.
            var body = dockerfile.Contains( "claude", StringComparison.Ordinal )
                ? "FROM base\nCOPY .g/update.timestamp C:\\docker-context\\update.timestamp\n"
                : "FROM base\nRUN echo hello\n";

            var path = Path.Combine( dockerfileDirectory, dockerfile );
            File.WriteAllText( path, body );
            invocations.AppendLine( CultureInfo.InvariantCulture, $"Copy-TimestampToContext '{path.Replace( "\\", "\\\\", StringComparison.Ordinal )}'" );
        }

        var script = $"""
                      $ErrorActionPreference = 'Stop'
                      $dockerContextDirectory = '{contextDirectory.Replace( "\\", "\\\\", StringComparison.Ordinal )}'
                      $script:TimestampFile = '{sourceTimestamp.Replace( "\\", "\\\\", StringComparison.Ordinal )}'

                      {ExtractFunction( "Get-DockerfileStem" )}

                      {ExtractFunction( "Get-ContextDirFor" )}

                      {ExtractFunction( "Test-BakesCacheBuster" )}

                      {ExtractFunction( "Copy-TimestampToContext" )}

                      {invocations}
                      """;

        RunPowerShell( executable, script );

        return contextDirectory;
    }

    /// <summary>
    /// The regression: a prefixed Claude leaf must receive the cache-buster in its own context directory, and the
    /// unprefixed <c>claude</c> directory must not be invented for it.
    /// </summary>
    [Fact]
    public void PrefixedClaudeImage_GetsTheTimestampInItsOwnContext()
    {
        var executable = FindPowerShell();

        if ( executable == null )
        {
            return;
        }

        var contextDirectory = StageTimestamp( executable, ["agent-claude.Dockerfile"] );

        Assert.True(
            File.Exists( Path.Combine( contextDirectory, "agent-claude", ".g", "update.timestamp" ) ),
            "The prefixed Claude leaf did not receive the cache-buster in its own context." );

        Assert.False(
            Directory.Exists( Path.Combine( contextDirectory, "claude" ) ),
            "The cache-buster was written to the unprefixed 'claude' context, which no image builds against." );
    }

    /// <summary>
    /// Several Claude leaves can coexist in one product, and each builds against its own context, so each needs a copy.
    /// An image that does not bake the cache-buster must not get one, so no stray file enters its context.
    /// </summary>
    [Fact]
    public void EveryImageThatBakesTheCacheBuster_GetsItsOwnCopy()
    {
        var executable = FindPowerShell();

        if ( executable == null )
        {
            return;
        }

        var contextDirectory = StageTimestamp(
            executable,
            ["claude.Dockerfile", "agent-claude.Dockerfile", "build.Dockerfile"] );

        Assert.True( File.Exists( Path.Combine( contextDirectory, "claude", ".g", "update.timestamp" ) ) );
        Assert.True( File.Exists( Path.Combine( contextDirectory, "agent-claude", ".g", "update.timestamp" ) ) );

        // 'build' does not declare the COPY, so nothing may be staged for it.
        Assert.False( Directory.Exists( Path.Combine( contextDirectory, "build", ".g" ) ) );
    }

    /// <summary>
    /// Guards the shape of the fix independently of whether PowerShell is available to execute it: the destination must
    /// be derived from the Dockerfile being built, never from a hardcoded directory name.
    /// </summary>
    [Fact]
    public void TimestampDestination_IsNotHardcodedToTheUnprefixedClaudeDirectory()
    {
        var script = ScriptText;

        Assert.DoesNotContain( """
                               Join-Path $dockerContextDirectory "claude"
                               """, script, StringComparison.Ordinal );

        // The copy is staged per built image, from the Dockerfile actually being built.
        Assert.Contains( "Copy-TimestampToContext $dfPath", script, StringComparison.Ordinal );
        Assert.Contains( "Join-Path (Get-ContextDirFor $dfPath) \".g\"", script, StringComparison.Ordinal );
    }

    /// <summary>
    /// The "is this a Claude leaf" decision governs tag rotation, local-only handling, and cache-buster staging. All
    /// three must key off the Dockerfile body, because the stem carries a product-defined prefix.
    /// </summary>
    [Fact]
    public void ClaudeLeafDetection_IsNotBasedOnTheDockerfileStem()
    {
        var script = ScriptText;

        Assert.DoesNotContain( "(Get-DockerfileStem $dfPath) -eq 'claude'", script, StringComparison.Ordinal );
        Assert.Contains( "$isClaudeLeaf = Test-BakesCacheBuster $dfPath", script, StringComparison.Ordinal );
    }
}
