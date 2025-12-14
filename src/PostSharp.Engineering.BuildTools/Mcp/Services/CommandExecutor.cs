// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Mcp.Models;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Mcp.Services;

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
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = $"-NoProfile -Command \"{EscapeForPowerShell( command )}\"",
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

            var stdout = await process.StandardOutput.ReadToEndAsync( cancellationToken );
            var stderr = await process.StandardError.ReadToEndAsync( cancellationToken );
            await process.WaitForExitAsync( cancellationToken );

            return CommandResult.Success( stdout, stderr, process.ExitCode );
        }
        catch ( Exception ex )
        {
            return CommandResult.Error( $"Execution error: {ex.Message}" );
        }
    }

    private static string EscapeForPowerShell( string command )
    {
        // Escape double quotes for PowerShell command line
        // Using backtick (`) as the PowerShell escape character
        return command
            .Replace( "\"", "`\"", StringComparison.Ordinal )
            .Replace( "$", "`$", StringComparison.Ordinal );
    }
}
