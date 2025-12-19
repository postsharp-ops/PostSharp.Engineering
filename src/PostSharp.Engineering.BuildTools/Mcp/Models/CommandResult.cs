// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Mcp.Models;

/// <summary>
/// Represents the result of a command execution through the MCP approval service.
/// </summary>
public sealed class CommandResult
{
    public required string Status { get; init; }

    public string? Output { get; init; }

    public string? Stderr { get; init; }

    public int ExitCode { get; init; }

    public string? RejectionReason { get; init; }

    public static CommandResult Rejected()
    {
        // Note: Reason is intentionally NOT included in the response to prevent
        // adaptive attacks where a compromised agent learns from rejection reasons.
        return new CommandResult { Status = "rejected", ExitCode = -1 };
    }

    public static CommandResult Error( string message, int exitCode = -1 )
    {
        return new CommandResult { Status = "error", ExitCode = exitCode, Stderr = message };
    }

    public static CommandResult Success( string output, string? stderr, int exitCode )
    {
        return new CommandResult { Status = exitCode == 0 ? "approved" : "error", Output = output, Stderr = stderr, ExitCode = exitCode };
    }
}