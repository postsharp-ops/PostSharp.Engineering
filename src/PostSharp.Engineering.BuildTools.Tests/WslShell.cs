// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Runs a POSIX shell script on the Linux engine of a Windows development machine, for a test whose subject only exists
/// there: a container that runs as root, and the ownership of what it leaves in a bind mount.
/// </summary>
/// <remarks>
/// A build agent has no WSL -- it runs one engine natively -- so a test that needs this reports success where it is
/// absent rather than failing the machine. That is the same bargain <see cref="DockerBuildWslHopTests"/> makes.
/// </remarks>
internal static class WslShell
{
    /// <summary>
    /// Runs <paramref name="script"/> with <c>sh</c> inside WSL and returns whether it could be run at all, which is
    /// what a caller skips on. <c>--exec</c> rather than <c>--</c>, so that nothing in the command line reaches a shell
    /// of the distribution before <c>sh</c> reads it.
    /// </summary>
    public static bool TryRun( string script, out int exitCode, out string output )
    {
        exitCode = 0;
        output = "";

        if ( !OperatingSystem.IsWindows() )
        {
            return false;
        }

        // Through a file, so that no quoting of the script survives being passed as an argument.
        var scriptFile = Path.Combine( Path.GetTempPath(), $"wsl-{Guid.NewGuid():N}.sh" );

        // LF, because it is read by a shell that treats CR as part of a word.
        File.WriteAllText( scriptFile, script.ReplaceLineEndings( "\n" ), new UTF8Encoding( false ) );

        try
        {
            var startInfo = new ProcessStartInfo( "wsl.exe" ) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };

            foreach ( var argument in new[] { "--exec", "sh", ToWslPath( scriptFile ) } )
            {
                startInfo.ArgumentList.Add( argument );
            }

            using var process = Process.Start( startInfo );

            if ( process == null )
            {
                return false;
            }

            // Both streams are read before the wait, because reading one to the end while the other fills its pipe
            // buffer deadlocks.
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            process.WaitForExit( 300_000 );

            output = standardOutput.GetAwaiter().GetResult() + standardError.GetAwaiter().GetResult();
            exitCode = process.ExitCode;

            return true;
        }
        catch ( Exception )
        {
            // No wsl.exe on this machine.
            return false;
        }
        finally
        {
            File.Delete( scriptFile );
        }
    }

    /// <summary>
    /// The path a Windows file has inside WSL: <c>C:\x\y</c> becomes <c>/mnt/c/x/y</c>.
    /// </summary>
    public static string ToWslPath( string path )
    {
        if ( path.Length > 2 && path[1] == ':' )
        {
            return $"/mnt/{char.ToLowerInvariant( path[0] )}/{path[3..].Replace( '\\', '/' )}";
        }

        return path.Replace( '\\', '/' );
    }
}
