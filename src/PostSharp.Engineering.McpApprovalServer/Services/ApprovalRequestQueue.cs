// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.McpApprovalServer.Mcp.Models;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.McpApprovalServer.Services;

/// <summary>
/// Thread-safe queue for managing pending approval requests.
/// </summary>
public sealed class ApprovalRequestQueue
{
    private readonly ConcurrentDictionary<string, ApprovalRequest> _pendingRequests = new();
    private readonly ConcurrentQueue<string> _requestOrder = new();

    /// <summary>
    /// Raised when a new request is added to the queue.
    /// </summary>
    public event EventHandler<ApprovalRequest>? RequestAdded;

    /// <summary>
    /// Raised when a request is completed (approved or rejected).
    /// </summary>
    public event EventHandler<string>? RequestCompleted;

    /// <summary>
    /// Raised when the queue changes (for tray icon updates).
    /// </summary>
    public event EventHandler? QueueChanged;

    /// <summary>
    /// Gets the number of pending requests.
    /// </summary>
    public int PendingCount => this._pendingRequests.Count;

    /// <summary>
    /// Gets whether there are any pending requests.
    /// </summary>
    public bool HasPendingRequests => !this._pendingRequests.IsEmpty;

    /// <summary>
    /// Enqueues a new approval request and returns a task that completes when the user responds.
    /// </summary>
    public Task<bool> EnqueueAsync(
        string command,
        string claimedPurpose,
        string workingDirectory,
        RiskAssessment combinedAssessment,
        RiskAssessment aiAssessment,
        RiskAssessment regexAssessment,
        CancellationToken cancellationToken = default )
    {
        var request = new ApprovalRequest
        {
            Id = Guid.NewGuid().ToString( "N" ),
            Command = command,
            ClaimedPurpose = claimedPurpose,
            WorkingDirectory = workingDirectory,
            GitBranch = GitHelper.GetBranch( workingDirectory ),
            CombinedAssessment = combinedAssessment,
            AiAssessment = aiAssessment,
            RegexAssessment = regexAssessment,
            ReceivedAt = DateTime.Now
        };

        this._pendingRequests.TryAdd( request.Id, request );
        this._requestOrder.Enqueue( request.Id );

        System.Diagnostics.Debug.WriteLine( $"[ApprovalRequestQueue] EnqueueAsync: Added request {request.Id}, Command: {request.Command}" );
        System.Diagnostics.Debug.WriteLine( $"[ApprovalRequestQueue] EnqueueAsync: Queue now has {this._pendingRequests.Count} pending requests" );

        // Handle cancellation
        cancellationToken.Register( () =>
        {
            if ( this._pendingRequests.TryRemove( request.Id, out var removed ) )
            {
                removed.CompletionSource.TrySetCanceled( cancellationToken );
                this.QueueChanged?.Invoke( this, EventArgs.Empty );
            }
        } );

        System.Diagnostics.Debug.WriteLine( "[ApprovalRequestQueue] EnqueueAsync: Firing RequestAdded event" );
        this.RequestAdded?.Invoke( this, request );
        System.Diagnostics.Debug.WriteLine( "[ApprovalRequestQueue] EnqueueAsync: Firing QueueChanged event" );
        this.QueueChanged?.Invoke( this, EventArgs.Empty );

        return request.CompletionSource.Task;
    }

    /// <summary>
    /// Gets the oldest pending request, or null if the queue is empty.
    /// </summary>
    public ApprovalRequest? GetOldestRequest()
    {
        while ( this._requestOrder.TryPeek( out var requestId ) )
        {
            if ( this._pendingRequests.TryGetValue( requestId, out var request ) )
            {
                return request;
            }

            // Request was already completed, remove from order queue
            this._requestOrder.TryDequeue( out _ );
        }

        return null;
    }

    /// <summary>
    /// Gets a specific request by ID.
    /// </summary>
    public ApprovalRequest? GetRequest( string requestId )
    {
        return this._pendingRequests.TryGetValue( requestId, out var request ) ? request : null;
    }

    /// <summary>
    /// Gets all pending requests.
    /// </summary>
    public ApprovalRequest[] GetAllPendingRequests()
    {
        return this._pendingRequests.Values.ToArray();
    }

    /// <summary>
    /// Completes a request with the user's decision.
    /// </summary>
    public void CompleteRequest( string requestId, bool approved )
    {
        if ( this._pendingRequests.TryRemove( requestId, out var request ) )
        {
            request.CompletionSource.TrySetResult( approved );
            this.RequestCompleted?.Invoke( this, requestId );
            this.QueueChanged?.Invoke( this, EventArgs.Empty );
        }
    }
}
