// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The concurrent draining of the two output streams of the Claude process, exercised as the real PowerShell
/// against a child that floods stderr.
/// </summary>
/// <remarks>
/// <para>
/// <c>Invoke-ClaudeOnce</c> redirects stdout and stderr. The stderr pipe holds a fixed buffer, 4 KB on Windows and
/// 64 KB on Linux. Reading stdout to the end while stderr is left unread deadlocks as soon as the child fills that
/// buffer: the child blocks on its next write to stderr, so it produces no more stdout, and the parent blocks for
/// ever on <c>ReadLine</c>.
/// </para>
/// <para>
/// The test runs the shipped function rather than asserting on its text, because the text of a correct fix and the
/// text of an incorrect one differ by the position of one statement. The child writes far more than either buffer
/// to stderr before it writes anything to stdout, which is the order that deadlocks.
/// </para>
/// </remarks>
public sealed class ClaudeStderrDrainTests : IDisposable
{
    // The apostrophe is deliberate. Every path below is interpolated into a single-quoted PowerShell literal, and a
    // user profile such as `O'Connor` puts one in the temporary path of a real machine. Keeping one here means the
    // escaping is exercised on every run rather than on the machine that happens to have such a profile.
    private readonly string _directory = Path.Combine( Path.GetTempPath(), $"stderr-drain-o'brien-{Guid.NewGuid():N}" );

    public ClaudeStderrDrainTests()
    {
        Directory.CreateDirectory( this._directory );
    }

    private static string ReadResource()
    {
        var assembly = typeof(EnvironmentVariableNames).Assembly;

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

    /// <summary>
    /// Runs a script in <c>pwsh</c> and kills it when it exceeds <paramref name="timeout"/>. The timeout is the
    /// assertion: the failure this test guards against is a hang, which without it would stop the whole test run
    /// instead of failing one test.
    /// </summary>
    private string RunPowerShell( string script, TimeSpan timeout )
    {
        var file = Path.Combine( this._directory, $"drain-{Guid.NewGuid():N}.ps1" );
        File.WriteAllText( file, script, new UTF8Encoding( false ) );

        var startInfo = new ProcessStartInfo( "pwsh" ) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };

        startInfo.ArgumentList.Add( "-NoProfile" );
        startInfo.ArgumentList.Add( "-NonInteractive" );
        startInfo.ArgumentList.Add( "-File" );
        startInfo.ArgumentList.Add( file );

        using var process = Process.Start( startInfo )!;

        // Both streams are read concurrently here for the same reason the script under test has to: this harness
        // would otherwise deadlock on the very output it is measuring.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if ( !process.WaitForExit( (int) timeout.TotalMilliseconds ) )
        {
            process.Kill( true );

            Assert.Fail(
                $"Invoke-ClaudeOnce did not return within {timeout.TotalSeconds:0} seconds. The child filled the stderr "
                + "pipe buffer and the reader is blocked on stdout, which is the deadlock of issue #120." );
        }

        // WaitForExit(int) does not wait for the redirected streams to be drained, so the tasks are awaited after it.
        var output = Task.WhenAll( outputTask, errorTask ).GetAwaiter().GetResult();

        Assert.True( process.ExitCode == 0, $"pwsh exited with {process.ExitCode}: {output[1]}" );

        return output[0];
    }

    [Fact]
    public void AChildThatFloodsStderrDoesNotBlockTheReaderOfStdout()
    {
        // Far more than the 64 KB of the largest of the two buffers, and written before anything reaches stdout.
        // That order is what deadlocks: the parent is already waiting on stdout when the child stops being able to
        // write to stderr.
        var childFile = Path.Combine( this._directory, "child.ps1" );

        File.WriteAllText(
            childFile,
            """
            [Console]::Error.Write( 'x' * 400000 )
            [Console]::Out.WriteLine( '{"type":"result","subtype":"success","is_error":false,"result":"<promptly-done/>","session_id":"the-session"}' )
            """,
            new UTF8Encoding( false ) );

        var logFile = Path.Combine( this._directory, "transcript.json" );
        var script = ReadResource();

        var harness = ExtractFunction( script, "Sanitize-ClaudeOutput" )
                      + "\n" + ExtractFunction( script, "ConvertFrom-ClaudeJsonLine" )
                      + "\n" + ExtractFunction( script, "Invoke-ClaudeOnce" )
                      + $$"""

                          $script:ClaudeExe = 'pwsh'
                          $result = Invoke-ClaudeOnce `
                              -Arguments '-NoProfile -NonInteractive -File "{{DockerBuildScript.Escape( childFile )}}"' `
                              -StdinContent '' `
                              -LogFile '{{DockerBuildScript.Escape( logFile )}}'
                          "EXIT=$($result.ExitCode) SENTINEL=$($result.Sentinel) SESSION=$($result.SessionId)"

                          """;

        var output = this.RunPowerShell( harness, TimeSpan.FromSeconds( 60 ) );

        // The stdout line the child wrote after the flood was read, parsed and reported, which it cannot be unless
        // stderr was drained while the loop was reading stdout.
        Assert.Contains( "EXIT=0 SENTINEL=done SESSION=the-session", output, StringComparison.Ordinal );
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete( this._directory, true );
        }
        catch ( IOException )
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }
}
