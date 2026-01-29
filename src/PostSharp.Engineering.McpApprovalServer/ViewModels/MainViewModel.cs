// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using CommunityToolkit.Mvvm.ComponentModel;
using PostSharp.Engineering.McpApprovalServer.Services;

namespace PostSharp.Engineering.McpApprovalServer.ViewModels;

/// <summary>
/// ViewModel for the main window (primarily manages tray icon state).
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ApprovalRequestQueue _queue;

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private bool _hasPendingRequests;

    public MainViewModel( ApprovalRequestQueue queue )
    {
        this._queue = queue;
        this._queue.QueueChanged += this.OnQueueChanged;
        this.UpdateState();
    }

    private void OnQueueChanged( object? sender, System.EventArgs e )
    {
        this.UpdateState();
    }

    private void UpdateState()
    {
        this.PendingCount = this._queue.PendingCount;
        this.HasPendingRequests = this._queue.HasPendingRequests;
    }
}
