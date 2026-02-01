// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.McpApprovalServer.Services;

/// <summary>
/// Helper class for git operations.
/// </summary>
internal static class GitHelper
{
    /// <summary>
    /// Gets the current git branch for a directory asynchronously.
    /// </summary>
    public static async Task<string?> GetBranchAsync( string workingDirectory, CancellationToken cancellationToken = default )
    {
        try
        {
            if ( !Directory.Exists( workingDirectory ) )
            {
                return "(directory not found)";
            }

            var output = await RunCommandAsync( workingDirectory, "rev-parse --abbrev-ref HEAD", cancellationToken );

            return string.IsNullOrWhiteSpace( output ) ? null : output.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Runs a git command and returns its output.
    /// </summary>
    public static async Task<string> RunCommandAsync(
        string workingDirectory,
        string arguments,
        CancellationToken cancellationToken = default )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start( startInfo );

        if ( process == null )
        {
            return string.Empty;
        }

        var output = await process.StandardOutput.ReadToEndAsync( cancellationToken );
        await process.WaitForExitAsync( cancellationToken );

        return output;
    }
}