// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.McpApprovalServer.Mcp.Models;
using PostSharp.Engineering.McpApprovalServer.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.McpApprovalServer.Mcp.Services;

/// <summary>
/// Executes commands via PowerShell on the host machine.
/// </summary>
public sealed class CommandExecutor
{
    // Suppress CA1822 - this is a DI service, keeping as instance method for consistency
#pragma warning disable CA1822
    public async Task<CommandResult> ExecuteAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken = default )
#pragma warning restore CA1822
    {
        // Write the command to a temp file to avoid escaping issues
        var tempFile = Path.Combine( Path.GetTempPath(), $"mcp-cmd-{Guid.NewGuid():N}.ps1" );

        try
        {
            await File.WriteAllTextAsync( tempFile, command, Encoding.UTF8, cancellationToken );

            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = $"-NoProfile -File \"{tempFile}\"",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start( startInfo );

            if ( process == null )
            {
                return CommandResult.Error( "Failed to start PowerShell process" );
            }

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            // Stream output to console in real-time
            var stdoutTask = ReadStreamAsync( process.StandardOutput, stdoutBuilder, "stdout", cancellationToken );
            var stderrTask = ReadStreamAsync( process.StandardError, stderrBuilder, "stderr", cancellationToken );

            await Task.WhenAll( stdoutTask, stderrTask );
            await process.WaitForExitAsync( cancellationToken );

            return CommandResult.Success( stdoutBuilder.ToString(), stderrBuilder.ToString(), process.ExitCode );
        }
        catch ( Exception ex )
        {
            TraceLogger.Logger.Error( "Command execution failed", ex );

            return CommandResult.Error( $"Execution error: {ex.Message}" );
        }
        finally
        {
            // Clean up temp file
            try
            {
                if ( File.Exists( tempFile ) )
                {
                    File.Delete( tempFile );
                }
            }
            catch ( Exception ex )
            {
                TraceLogger.Logger.Error( $"Failed to delete temp file {tempFile}: {ex.Message}" );
            }
        }
    }

    private static async Task ReadStreamAsync(
        StreamReader reader,
        StringBuilder output,
        string streamName,
        CancellationToken cancellationToken )
    {
        while ( !cancellationToken.IsCancellationRequested )
        {
            var line = await reader.ReadLineAsync( cancellationToken );

            if ( line == null )
            {
                break;
            }

            output.AppendLine( line );
        }
    }
}