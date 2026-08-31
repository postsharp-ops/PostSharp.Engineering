// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The resume loop's "is this run stuck?" guard, exercised as the real PowerShell rather than as a description of
/// it.
/// </summary>
/// <remarks>
/// <para>
/// This guard ended two real scheduled runs, both times wrongly, and each time for a different reason. It counted
/// an iteration the API had refused (an <c>API Error: 529 Overloaded</c>) as evidence that the model was looping
/// without achieving anything, when in fact the model never ran. And it measured progress as a git commit, which is
/// the right signal for an agent whose job is to change code and the wrong one for an agent whose job is anything
/// else: the nightly CEIP triage build writes rows to a database, so every iteration of a healthy run reported no
/// progress.
/// </para>
/// <para>
/// The two functions are extracted from the embedded resource and run in <c>pwsh</c>, because dot-sourcing the
/// script would execute it, and asserting on its text would only prove the text.
/// </para>
/// </remarks>
public sealed class ClaudeProgressGuardTests : IDisposable
{
    private readonly string _directory = Path.Combine( Path.GetTempPath(), $"progress-guard-{Guid.NewGuid():N}" );

    public ClaudeProgressGuardTests()
    {
        Directory.CreateDirectory( this._directory );
    }

    private static string ReadResource()
    {
        var assembly = typeof(BuildTools.EnvironmentVariableNames).Assembly;

        var name = assembly.GetManifestResourceNames().Single( n => n.EndsWith( "RunClaude.ps1", StringComparison.Ordinal ) );

        using var stream = assembly.GetManifestResourceStream( name )!;
        using var reader = new StreamReader( stream );

        return reader.ReadToEnd();
    }

    /// <summary>
    /// Lifts one <c>function Name { ... }</c> out of the script by matching braces, so the test runs the shipped
    /// definition and not a copy of it.
    /// </summary>
    private static string ExtractFunction( string script, string name )
    {
        var start = script.IndexOf( $"function {name} {{", StringComparison.Ordinal );

        Assert.True( start >= 0, $"'{name}' is not in RunClaude.ps1 any more; this test guards a function that has been renamed or removed." );

        var depth = 0;

        for ( var i = script.IndexOf( '{', start ); i < script.Length; i++ )
        {
            if ( script[i] == '{' )
            {
                depth++;
            }
            else if ( script[i] == '}' )
            {
                depth--;

                if ( depth == 0 )
                {
                    return script.Substring( start, i - start + 1 );
                }
            }
        }

        throw new InvalidOperationException( $"Unbalanced braces while extracting '{name}'." );
    }

    private static IReadOnlyList<string> RunPowerShell( string script )
    {
        var file = Path.Combine( Path.GetTempPath(), $"guard-{Guid.NewGuid():N}.ps1" );
        File.WriteAllText( file, script, new UTF8Encoding( false ) );

        try
        {
            var startInfo = new ProcessStartInfo( "pwsh" )
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            };

            startInfo.ArgumentList.Add( "-NoProfile" );
            startInfo.ArgumentList.Add( "-NonInteractive" );
            startInfo.ArgumentList.Add( "-File" );
            startInfo.ArgumentList.Add( file );

            using var process = Process.Start( startInfo )!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True( process.ExitCode == 0, $"pwsh exited with {process.ExitCode}: {error}" );

            return output.Split( '\n' ).Select( l => l.Trim() ).Where( l => l.Length > 0 ).ToList();
        }
        finally
        {
            File.Delete( file );
        }
    }

    /// <summary>
    /// The regression. Each of these is a turn that never ran, and calling one of them "no progress" is what ended
    /// the runs of 2026-07-29 and 2026-07-30 after two iterations apiece.
    /// </summary>
    [Theory]
    [InlineData( "API Error: 529 Overloaded. This is a server-side issue, usually temporary", true )]
    [InlineData( "API Error: 429 Too Many Requests", true )]
    [InlineData( "API Error: 503 Service Unavailable", true )]
    [InlineData( """{"type":"overloaded_error","message":"Overloaded"}""", true )]
    [InlineData( """{"type":"rate_limit_error"}""", true )]

    // A turn that ran and reported. Whether it achieved anything is the other guard's business, and these must
    // still reach it: treating an ordinary failure as transient would let a genuinely stuck run go round for ever.
    [InlineData( "I could not find the file and have stopped.", false )]
    [InlineData( "API Error: 400 Bad Request", false )]
    [InlineData( "The build failed with error code 529 in the compiler output.", false )]
    [InlineData( "", false )]
    public void ATurnTheApiRefusedIsToldFromATurnThatAchievedNothing( string resultText, bool expected )
    {
        var script = ExtractFunction( ReadResource(), "Test-TransientApiFailure" )
                     + $"\nif ( Test-TransientApiFailure -Text @'\n{resultText}\n'@ ) {{ 'TRANSIENT' }} else {{ 'ORDINARY' }}\n";

        Assert.Equal( expected ? "TRANSIENT" : "ORDINARY", RunPowerShell( script ).Last() );
    }

    private static string Fingerprint( string watched, params string[] extraLines )
    {
        var resource = ReadResource();

        var script = ExtractFunction( resource, "Get-RepoHeads" )
                     + "\n"
                     + ExtractFunction( resource, "Get-ProgressFingerprint" )
                     + "\n"
                     + string.Join( "\n", extraLines )
                     + $"\nGet-ProgressFingerprint -Repos @() -Paths @('{watched}') -ExcludePath '{Path.Combine( watched, "logs" )}'\n";

        return RunPowerShell( script ).Last();
    }

    /// <summary>
    /// Work that lands in a directory rather than in a commit is progress. Without this the nightly triage run,
    /// which writes to a database and to <c>artifacts</c> and never commits, reported no progress on every
    /// iteration of a perfectly healthy session.
    /// </summary>
    [Fact]
    public void AFileWrittenWhereTheBuildSaidItsWorkLandsIsProgress()
    {
        var before = Fingerprint( this._directory );

        var after = Fingerprint(
            this._directory,
            $"Set-Content -Path '{Path.Combine( this._directory, "log-cluster.md" )}' -Value 'a log entry'" );

        Assert.NotEqual( before, after );
    }

    /// <summary>
    /// And a turn that changed nothing anywhere still reads as no progress, which is the whole point of the guard.
    /// </summary>
    [Fact]
    public void ADirectoryThatDidNotChangeIsNotProgress()
    {
        File.WriteAllText( Path.Combine( this._directory, "existing.md" ), "unchanged" );

        Assert.Equal( Fingerprint( this._directory ), Fingerprint( this._directory ) );
    }

    /// <summary>
    /// The trap in watching a directory: this loop writes its own transcript under <c>artifacts\logs</c> on every
    /// iteration. Counting that would make every iteration look productive and the guard would never fire again,
    /// which is a worse failure than the one it is here to fix, and a silent one.
    /// </summary>
    [Fact]
    public void TheLoopsOwnTranscriptDoesNotCountAsProgress()
    {
        var logs = Path.Combine( this._directory, "logs" );
        Directory.CreateDirectory( logs );

        var before = Fingerprint( this._directory );

        var after = Fingerprint(
            this._directory,
            $"Set-Content -Path '{Path.Combine( logs, "claude-2026-07-30-104235.log.json" )}' -Value '[]'" );

        Assert.Equal( before, after );
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete( this._directory, true );
        }
        catch ( IOException )
        {
            // A temp directory is disposable; a held file is not worth failing a green run over.
        }
    }
}
