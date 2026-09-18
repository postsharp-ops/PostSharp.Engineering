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
    /// Escapes a value for embedding in a single-quoted PowerShell literal. A single quote is the only character
    /// that needs it: PowerShell takes everything else in a single-quoted string verbatim, backslashes included.
    /// </summary>
    public static string Escape( string value ) => value.Replace( "'", "''", StringComparison.Ordinal );
}
