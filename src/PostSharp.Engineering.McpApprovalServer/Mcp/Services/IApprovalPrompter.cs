// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.McpApprovalServer.Mcp.Models;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.McpApprovalServer.Mcp.Services;

/// <summary>
/// Interface for prompting users for command approval.
/// Implementations can be console-based or GUI-based.
/// </summary>
public interface IApprovalPrompter
{
    /// <summary>
    /// Requests user approval for a command execution.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="claimedPurpose">The purpose claimed by the requestor.</param>
    /// <param name="workingDirectory">The directory where the command would be executed.</param>
    /// <param name="combinedAssessment">The combined risk assessment from all analyzers.</param>
    /// <param name="aiAssessment">The AI-based risk assessment.</param>
    /// <param name="regexAssessment">The regex-based risk assessment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the user approves the command, false otherwise.</returns>
    Task<bool> RequestApprovalAsync(
        string command,
        string claimedPurpose,
        string workingDirectory,
        RiskAssessment combinedAssessment,
        RiskAssessment aiAssessment,
        RiskAssessment regexAssessment,
        CancellationToken cancellationToken = default );
}
