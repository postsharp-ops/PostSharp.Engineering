// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.McpApprovalServer.Mcp.Models;
using PostSharp.Engineering.McpApprovalServer.Mcp.Services;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.McpApprovalServer.Services;

/// <summary>
/// GUI-based implementation of <see cref="IApprovalPrompter"/> that uses the approval queue
/// to display requests in individual windows.
/// </summary>
public sealed class GuiApprovalPrompter : IApprovalPrompter
{
    private readonly ApprovalRequestQueue _queue;
    private readonly TrayIconService _trayIconService;

    public GuiApprovalPrompter( ApprovalRequestQueue queue, TrayIconService trayIconService )
    {
        this._queue = queue;
        this._trayIconService = trayIconService;
    }

    /// <inheritdoc />
    public async Task<bool> RequestApprovalAsync(
        string command,
        string claimedPurpose,
        string workingDirectory,
        RiskAssessment combinedAssessment,
        RiskAssessment aiAssessment,
        RiskAssessment regexAssessment,
        CancellationToken cancellationToken = default )
    {
        // Auto-approve LOW risk commands when combined assessment recommends approval
        if ( combinedAssessment.Level == RiskLevel.Low && combinedAssessment.Recommendation == Recommendation.Approve )
        {
            // Notify tray icon that we're processing (briefly show blue icon)
            this._trayIconService.NotifyProcessingStarted();

            try
            {
                // Small delay so the icon change is visible
                await Task.Delay( 500, cancellationToken );

                return true;
            }
            finally
            {
                this._trayIconService.NotifyProcessingCompleted();
            }
        }

        // Queue the request for user approval
        return await this._queue.EnqueueAsync(
            command,
            claimedPurpose,
            workingDirectory,
            combinedAssessment,
            aiAssessment,
            regexAssessment,
            cancellationToken );
    }
}
