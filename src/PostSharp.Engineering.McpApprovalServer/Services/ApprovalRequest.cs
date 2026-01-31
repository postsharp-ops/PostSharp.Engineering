// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.McpApprovalServer.Mcp.Models;
using System;
using System.Threading.Tasks;

namespace PostSharp.Engineering.McpApprovalServer.Services;

/// <summary>
/// Represents a pending approval request.
/// </summary>
public sealed class ApprovalRequest
{
    public required string Id { get; init; }

    public required string Command { get; init; }

    public required string ClaimedPurpose { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string GitBranch { get; init; }

    public required RiskAssessment CombinedAssessment { get; init; }

    public required RiskAssessment AiAssessment { get; init; }

    public required RiskAssessment RegexAssessment { get; init; }

    public required DateTime ReceivedAt { get; init; }

    public TaskCompletionSource<bool> CompletionSource { get; } = new();
}