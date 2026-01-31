// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.McpApprovalServer.Mcp.Models;
using PostSharp.Engineering.McpApprovalServer.Mcp.Services;
using PostSharp.Engineering.McpApprovalServer.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace PostSharp.Engineering.McpApprovalServer.ViewModels;

/// <summary>
/// ViewModel for the history window with auto-update support.
/// </summary>
public sealed class HistoryViewModel : IDisposable
{
    private readonly CommandHistoryService _historyService;
    private readonly ApprovalRequestQueue _requestQueue;
    private bool _disposed;

    public HistoryViewModel( CommandHistoryService historyService, ApprovalRequestQueue requestQueue )
    {
        this._historyService = historyService;
        this._requestQueue = requestQueue;

        this.History = new ObservableCollection<HistoryItemViewModel>();

        // Initial load
        this.RefreshHistory();

        // Subscribe to updates
        this._historyService.HistoryUpdated += this.OnHistoryUpdated;
        this._requestQueue.RequestAdded += this.OnRequestAdded;
        this._requestQueue.RequestCompleted += this.OnRequestCompleted;
        this._requestQueue.QueueChanged += this.OnQueueChanged;
    }

    public ObservableCollection<HistoryItemViewModel> History { get; }

    private void OnHistoryUpdated( object? sender, EventArgs e )
    {
        Debug.WriteLine( "[HistoryViewModel] OnHistoryUpdated event received" );
        Application.Current.Dispatcher.Invoke( this.RefreshHistory );
    }

    private void OnRequestAdded( object? sender, ApprovalRequest e )
    {
        Debug.WriteLine( $"[HistoryViewModel] OnRequestAdded event received: {e.Command}" );
        Application.Current.Dispatcher.Invoke( this.RefreshHistory );
    }

    private void OnRequestCompleted( object? sender, string e )
    {
        Debug.WriteLine( $"[HistoryViewModel] OnRequestCompleted event received: {e}" );
        Application.Current.Dispatcher.Invoke( this.RefreshHistory );
    }

    private void OnQueueChanged( object? sender, EventArgs e )
    {
        Debug.WriteLine( "[HistoryViewModel] OnQueueChanged event received" );
        Application.Current.Dispatcher.Invoke( this.RefreshHistory );
    }

    private void RefreshHistory()
    {
        this.History.Clear();

        // Add pending requests first (status = PENDING)
        var pendingRequests = this._requestQueue.GetAllPendingRequests()
            .OrderBy( r => r.ReceivedAt )
            .ToList();

        Debug.WriteLine( $"[HistoryViewModel] RefreshHistory: Found {pendingRequests.Count} pending requests" );

        foreach ( var request in pendingRequests )
        {
            Debug.WriteLine( $"[HistoryViewModel]   Adding pending: {request.Command}" );
            this.History.Add( new HistoryItemViewModel( request ) );
        }

        // Add completed history items (most recent first)
        var records = this._historyService.GetHistory()
            .OrderByDescending( r => r.Timestamp )
            .ToList();

        Debug.WriteLine( $"[HistoryViewModel] RefreshHistory: Found {records.Count} completed records" );

        foreach ( var record in records )
        {
            this.History.Add( new HistoryItemViewModel( record ) );
        }

        Debug.WriteLine( $"[HistoryViewModel] RefreshHistory: Total items in History: {this.History.Count}" );
    }

    public void Dispose()
    {
        if ( this._disposed )
        {
            return;
        }

        this._disposed = true;

        this._historyService.HistoryUpdated -= this.OnHistoryUpdated;
        this._requestQueue.RequestAdded -= this.OnRequestAdded;
        this._requestQueue.RequestCompleted -= this.OnRequestCompleted;
        this._requestQueue.QueueChanged -= this.OnQueueChanged;
    }
}

/// <summary>
/// ViewModel for a single history item (can be pending or completed).
/// </summary>
public sealed class HistoryItemViewModel
{
    private readonly CommandRecord? _record;
    private readonly ApprovalRequest? _pendingRequest;

    public HistoryItemViewModel( CommandRecord record )
    {
        this._record = record;
        this._pendingRequest = null;
        this.GitBranch = GitHelper.GetBranch( record.WorkingDirectory );
    }

    public HistoryItemViewModel( ApprovalRequest pendingRequest )
    {
        this._record = null;
        this._pendingRequest = pendingRequest;
        this.GitBranch = GitHelper.GetBranch( pendingRequest.WorkingDirectory );
    }

    public bool IsPending => this._pendingRequest != null;

    public bool Approved => this._record?.Approved ?? false;

    public string Timestamp
    {
        get
        {
            if ( this._pendingRequest != null )
            {
                return this._pendingRequest.ReceivedAt.ToString( "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture );
            }

            return this._record!.Timestamp.ToLocalTime().ToString( "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture );
        }
    }

    public string Status
    {
        get
        {
            if ( this._pendingRequest != null )
            {
                return "PENDING";
            }

            return this._record!.Approved ? "APPROVED" : "REJECTED";
        }
    }

    public string Command => this._pendingRequest?.Command ?? this._record!.Command;

    public string ClaimedPurpose => this._pendingRequest?.ClaimedPurpose ?? this._record!.ClaimedPurpose;

    public string WorkingDirectory => this._pendingRequest?.WorkingDirectory ?? this._record!.WorkingDirectory;

    public string GitBranch { get; }

    public string ExitCodeDisplay
    {
        get
        {
            if ( this._pendingRequest != null )
            {
                return "-";
            }

            return this._record!.ExitCode?.ToString( CultureInfo.InvariantCulture ) ?? "-";
        }
    }
}
