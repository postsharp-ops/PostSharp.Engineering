// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PostSharp.Engineering.BuildTools.Build.Model;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Gives the tests a way to exercise individual functions of <c>DockerBuild.ps1</c> without Docker and without a
/// repository: a function is lifted verbatim out of the shipped script and run on its own in PowerShell.
/// </summary>
internal static class DockerBuildScript
{
    /// <summary>
    /// Reads the script from the assembly rather than from the working tree. This is the copy that
    /// <c>generate-scripts</c> extracts into every consuming repository, so it is the artifact whose behaviour
    /// matters, and it is reachable regardless of where the test host runs from.
    /// </summary>
    public static string Text
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
    public static string ExtractFunction( string name )
    {
        var match = Regex.Match(
            Text,
            @"^    function\s+" + Regex.Escape( name ) + @"\b.*?^    \}",
            RegexOptions.Multiline | RegexOptions.Singleline );

        Assert.True( match.Success, $"Could not extract the '{name}' function from DockerBuild.ps1." );

        // Strip the leading indentation so the extracted text is valid at the top level of a script.
        return Regex.Replace( match.Value, "^    ", "", RegexOptions.Multiline );
    }

    /// <summary>
    /// Extracts a <c>function</c> block declared at the top level of the script, i.e. one that is not indented and is
    /// closed by a brace in the first column. <see cref="ExtractFunction"/> handles the ones nested in a block.
    /// </summary>
    /// <remarks>
    /// The end of the function is found by reading lines rather than with one expression, because a function that
    /// builds PowerShell for the container to run holds that text in a here-string, and the braces of the text inside
    /// it sit in the first column too. A pattern ending at the first such brace cut the function in half and left the
    /// here-string unterminated, which fails as a parse error in the harness rather than as a wrong assertion.
    /// </remarks>
    public static string ExtractTopLevelFunction( string name )
    {
        var lines = Text.ReplaceLineEndings( "\n" ).Split( '\n' );
        var header = $"function {name}";

        var start = Array.FindIndex(
            lines,
            l => l.StartsWith( header, StringComparison.Ordinal )
                 && (l.Length == header.Length || !(char.IsLetterOrDigit( l[header.Length] ) || l[header.Length] == '-')) );

        Assert.True( start >= 0, $"Could not find the top-level '{name}' function in DockerBuild.ps1." );

        // The terminator of the here-string that is open, or null outside one. A here-string opens at the end of a line
        // and its terminator is the first thing on a line, which is what makes this readable one line at a time.
        string? hereStringTerminator = null;

        for ( var i = start + 1; i < lines.Length; i++ )
        {
            var line = lines[i];

            if ( hereStringTerminator != null )
            {
                if ( line.StartsWith( hereStringTerminator, StringComparison.Ordinal ) )
                {
                    hereStringTerminator = null;
                }

                continue;
            }

            var trimmed = line.TrimEnd();

            if ( trimmed.EndsWith( "@'", StringComparison.Ordinal ) )
            {
                hereStringTerminator = "'@";
            }
            else if ( trimmed.EndsWith( "@\"", StringComparison.Ordinal ) )
            {
                hereStringTerminator = "\"@";
            }
            else if ( line == "}" )
            {
                return string.Join( "\n", lines[start..(i + 1)] );
            }
        }

        throw new InvalidOperationException( $"The top-level '{name}' function of DockerBuild.ps1 is not closed." );
    }

    /// <summary>
    /// Returns the PowerShell executable to run the extracted functions with, or <c>null</c> when the host has none.
    /// A test that gets <c>null</c> reports success rather than failing a machine that cannot run PowerShell at all.
    /// </summary>
    public static string? FindPowerShell()
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

    /// <summary>
    /// Runs a script through PowerShell and returns everything it wrote. A non-zero exit code fails the calling test
    /// with the whole output attached, because a PowerShell error is reported in the text and not in the exit code
    /// alone.
    /// </summary>
    public static string Run( string executable, string script )
    {
        var (exitCode, output) = TryRun( executable, script );

        Assert.True( exitCode == 0, $"PowerShell exited with {exitCode}:\n{output}" );

        return output;
    }

    /// <summary>
    /// Runs a script through PowerShell and returns its exit code along with everything it wrote, for a test whose
    /// subject is the exit code -- a clean-up that must fail the build rather than report what it could not do.
    /// </summary>
    public static (int ExitCode, string Output) TryRun( string executable, string script )
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

            return (process.ExitCode, output);
        }
        finally
        {
            File.Delete( scriptFile );
        }
    }

    /// <summary>
    /// Escapes a value for embedding in a single-quoted PowerShell literal. A single quote is the only character
    /// that needs it: PowerShell takes everything else in a single-quoted string verbatim, backslashes included.
    /// </summary>
    public static string Escape( string value ) => value.Replace( "'", "''", StringComparison.Ordinal );
}
