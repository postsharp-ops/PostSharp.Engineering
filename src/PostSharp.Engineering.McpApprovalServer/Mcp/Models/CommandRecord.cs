// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.McpApprovalServer.Mcp.Models;

/// <summary>
/// Represents a record of a command that was requested through the MCP approval service.
/// </summary>
public sealed class CommandRecord
{
    public required DateTime Timestamp { get; init; }

    public required string Command { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string ClaimedPurpose { get; init; }

    public required bool Approved { get; init; }

    public int? ExitCode { get; init; }

    public string? Output { get; init; }
}