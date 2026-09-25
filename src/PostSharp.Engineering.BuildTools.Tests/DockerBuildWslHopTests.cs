// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The hop that <c>DockerBuild.ps1</c> makes into WSL when Linux containers are requested on a Windows development
/// machine, and the two ways it used to corrupt the result of the build.
/// </summary>
/// <remarks>
/// <para>
/// The shell ate the arguments. <c>wsl.exe --</c> hands the command line to the default shell of the distribution,
/// which expands every <c>$</c> in it before <c>pwsh</c> reads it. A Docker test whose command ended in
/// <c>exit $?</c> reached the container as <c>exit 0</c>, so a scenario that failed was reported as passed.
/// </para>
/// <para>
/// <c>pwsh -Command</c> lost the exit code. It takes the exit code of the process from the success of the last
/// statement, which is 0 or 1, so every other exit code arrived as 1. <c>RunDockerTests.ps1</c> reads exit code 4 as
/// "this test skipped", so a test that skips on Linux was reported as failed.
/// </para>
/// <para>
/// The two are entangled, which is why they are tested together: appending <c>; exit $LASTEXITCODE</c> while the
/// hop still went through a shell would have made the failure worse rather than better, because the shell expands
/// that variable to nothing and the command then exits with the status of the statement before it, which is 0. The
/// three states were measured: through a shell with the suffix gives 0, through a shell without it gives 0, and
/// without a shell but without the suffix gives 1. Only both together give the real exit code.
/// </para>
/// <para>
/// A build agent runs the engine natively and makes no hop, so neither defect was ever visible in continuous
/// integration. They are visible exactly where a Docker test is written and checked.
/// </para>
/// </remarks>
public sealed class DockerBuildWslHopTests : IDisposable
{
    private readonly string _directory = Path.Combine( Path.GetTempPath(), $"wsl-hop-{Guid.NewGuid():N}" );

    public DockerBuildWslHopTests()
    {
        Directory.CreateDirectory( this._directory );
    }

    private static string ReadResource( string fileName )
    {
        var name = $"PostSharp.Engineering.BuildTools.Resources.{fileName}";

        using var stream = typeof(EnvironmentVariableNames).Assembly.GetManifestResourceStream( name )
                           ?? throw new InvalidOperationException( $"Cannot find the embedded resource '{name}'." );

        using var reader = new StreamReader( stream );

        return reader.ReadToEnd();
    }

    /// <summary>
    /// The rule, checked on any machine. The end-to-end test below is the proof, but it only runs where WSL is
    /// installed, and a build agent has none.
    /// </summary>
    [Theory]
    [InlineData( "DockerBuild.ps1" )]
    [InlineData( "RunDockerTests.ps1" )]
    public void NoInvocationOfWslGoesThroughAShell( string fileName )
    {
        // `wsl.exe --` followed by anything other than `exec`. The point is the separator that hands the rest of the
        // line to the default shell of the distribution.
        var matches = Regex.Matches( ReadResource( fileName ), @"wsl\.exe\s+--(?!exec)\S*" )
            .Select( m => m.Value )
            .ToArray();

        Assert.True(
            matches.Length == 0,
            $"{fileName} invokes wsl.exe through the default shell ({string.Join( ", ", matches )}), which expands "
            + "every `$` in the command line before the program reads it. Use `wsl.exe --exec`." );
    }

    /// <summary>
    /// Runs the hop as the shipped script builds and invokes it, against a scenario that prints what it received and
    /// exits with a code that is neither 0 nor 1.
    /// </summary>
    [Fact]
    public void TheCommandAndTheExitCodeSurviveTheHop()
    {
        var windowsPowerShell = DockerBuildScript.FindPowerShell();

        if ( windowsPowerShell == null || !TryGetWslPowerShell( out var wslPwsh ) )
        {
            // Neither WSL nor a PowerShell inside it is a failure of this rule, which the theory above still checks.
            return;
        }

        // The scenario that stands in for the copy of DockerBuild.ps1 running inside WSL. 4 is the code that
        // RunDockerTests.ps1 reads as "skipped", and the one that came back as 1.
        var scenario = Path.Combine( this._directory, "scenario.ps1" );

        File.WriteAllText(
            scenario,
            """
            param([string]$Probe)
            Write-Output "PROBE=$Probe"
            exit 4
            """,
            new UTF8Encoding( false ) );

        // A value holding what a shell would act on. It must reach the scenario unchanged.
        const string probe = "LITERAL-$HOME-$?";

        // The harness sets up exactly what the shipped script has in scope at the hop, then runs the shipped lines:
        // the construction of $wslCommand and the invocation of wsl.exe. Both are lifted from the script, so this
        // fails if either regresses.
        var harness = $$"""
                        {{DockerBuildScript.ExtractTopLevelFunction( "ConvertTo-PowerShellLiteral" )}}

                        {{DockerBuildScript.ExtractTopLevelFunction( "ConvertTo-WslHostPath" )}}

                        $wslPwsh = '{{DockerBuildScript.Escape( wslPwsh )}}'
                        $PSCommandPath = '{{DockerBuildScript.Escape( scenario )}}'
                        $wslArguments = @( ( ConvertTo-PowerShellLiteral '{{DockerBuildScript.Escape( probe )}}' ) )

                        {{ExtractShippedLines()}}

                        "OBSERVED=$LASTEXITCODE"
                        """;

        var output = DockerBuildScript.Run( windowsPowerShell, harness );

        Assert.Contains( $"PROBE={probe}", output, StringComparison.Ordinal );
        Assert.Contains( "OBSERVED=4", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// Lifts the construction of <c>$wslCommand</c> and the invocation of <c>wsl.exe</c> out of the shipped script,
    /// as one block, so the test runs what the script runs rather than a copy of it.
    /// </summary>
    private static string ExtractShippedLines()
    {
        var text = DockerBuildScript.Text;

        var start = text.IndexOf( "$wslCommand =", StringComparison.Ordinal );
        Assert.True( start >= 0, "The assignment of $wslCommand is not in DockerBuild.ps1 any more." );

        var invocation = text.IndexOf( "& wsl.exe", start, StringComparison.Ordinal );
        Assert.True( invocation > start, "The invocation of wsl.exe no longer follows the assignment of $wslCommand." );

        var end = text.IndexOf( '\n', invocation );
        Assert.True( end > invocation, "The invocation of wsl.exe is the last line of DockerBuild.ps1." );

        return text[start..end];
    }

    private static bool TryGetWslPowerShell( out string path )
    {
        path = "";

        if ( !OperatingSystem.IsWindows() )
        {
            return false;
        }

        try
        {
            if ( RunWsl( ["--exec", "sh", "-c", "command -v pwsh"], out var output ) != 0 )
            {
                return false;
            }

            path = output.Trim();

            return path.Length > 0;
        }
        catch ( Exception )
        {
            // No wsl.exe on this machine.
            return false;
        }
    }

    private static int RunWsl( string[] arguments, out string output )
    {
        var startInfo = new ProcessStartInfo( "wsl.exe" ) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };

        foreach ( var argument in arguments )
        {
            startInfo.ArgumentList.Add( argument );
        }

        using var process = Process.Start( startInfo )!;

        // Both streams are read before the wait, because reading one to the end while the other fills its pipe
        // buffer deadlocks.
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        process.WaitForExit( 120_000 );

        output = standardOutput.GetAwaiter().GetResult() + standardError.GetAwaiter().GetResult();

        return process.ExitCode;
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
