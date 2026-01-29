// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Hardcodet.Wpf.TaskbarNotification;
using PostSharp.Engineering.McpApprovalServer.ViewModels;
using PostSharp.Engineering.McpApprovalServer.Views;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PostSharp.Engineering.McpApprovalServer.Services;

/// <summary>
/// Manages the system tray icon and its interactions.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly ApprovalRequestQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly Mcp.Services.CommandHistoryService _historyService;
    private TaskbarIcon? _taskbarIcon;
    private ImageSource? _normalIcon;
    private ImageSource? _pendingIcon;
    private ImageSource? _processingIcon;
    private readonly Dictionary<string, ApprovalWindow> _openWindows = new();
    private HistoryWindow? _historyWindow;
    private bool _disposed;
    private int _processingCount;

    public TrayIconService( ApprovalRequestQueue queue, IServiceProvider serviceProvider, Mcp.Services.CommandHistoryService historyService )
    {
        this._queue = queue;
        this._serviceProvider = serviceProvider;
        this._historyService = historyService;
    }

    /// <summary>
    /// Notifies the tray icon that a request is being processed (auto-approved).
    /// </summary>
    public void NotifyProcessingStarted()
    {
        System.Diagnostics.Debug.WriteLine( "[TrayIcon] NotifyProcessingStarted called" );

        Application.Current.Dispatcher.Invoke( () =>
        {
            this._processingCount++;
            System.Diagnostics.Debug.WriteLine( $"[TrayIcon] ProcessingCount is now {this._processingCount}" );
            this.UpdateIcon();
        } );
    }

    /// <summary>
    /// Notifies the tray icon that processing has completed.
    /// </summary>
    public void NotifyProcessingCompleted()
    {
        System.Diagnostics.Debug.WriteLine( "[TrayIcon] NotifyProcessingCompleted called" );

        Application.Current.Dispatcher.Invoke( () =>
        {
            this._processingCount = Math.Max( 0, this._processingCount - 1 );
            System.Diagnostics.Debug.WriteLine( $"[TrayIcon] ProcessingCount is now {this._processingCount}" );
            this.UpdateIcon();
        } );
    }

    /// <summary>
    /// Initializes the tray icon and sets up event handlers.
    /// </summary>
    public void Initialize()
    {
        // Load icons (or create default ones)
        this._normalIcon = LoadIcon( "pack://application:,,,/Resources/Icons/tray-normal.ico" )
                           ?? CreateDefaultIcon( System.Drawing.Color.Green );

        this._pendingIcon = LoadIcon( "pack://application:,,,/Resources/Icons/tray-pending.ico" )
                            ?? CreateDefaultIcon( System.Drawing.Color.Orange );

        this._processingIcon = LoadIcon( "pack://application:,,,/Resources/Icons/tray-processing.ico" )
                               ?? CreateDefaultIcon( System.Drawing.Color.DodgerBlue );

        // Create context menu
        var contextMenu = new ContextMenu();

        var showMenuItem = new MenuItem { Header = "Show Oldest Request" };
        showMenuItem.Click += this.OnShowOldestRequest;
        contextMenu.Items.Add( showMenuItem );

        contextMenu.Items.Add( new Separator() );

        var exitMenuItem = new MenuItem { Header = "Exit" };
        exitMenuItem.Click += this.OnExit;
        contextMenu.Items.Add( exitMenuItem );

        // Create taskbar icon
        this._taskbarIcon = new TaskbarIcon
        {
            IconSource = this._normalIcon,
            ToolTipText = "MCP Approval Server - Ready",
            ContextMenu = contextMenu
        };

        // Handle left-click to show oldest request
        this._taskbarIcon.TrayLeftMouseDown += this.OnTrayLeftClick;

        // Subscribe to queue events
        this._queue.RequestAdded += this.OnRequestAdded;
        this._queue.QueueChanged += this.OnQueueChanged;
    }

    private static ImageSource? LoadIcon( string uri )
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri( uri );
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch
        {
            // Icon not found - return null so fallback is used
            return null;
        }
    }

    private static ImageSource CreateDefaultIcon( System.Drawing.Color color )
    {
        // Create a colored filled square icon (32x32 for better visibility)
        const int size = 32;
        using var bitmap = new Bitmap( size, size );
        using var graphics = Graphics.FromImage( bitmap );

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear( System.Drawing.Color.Transparent );

        // Fill the entire icon with the color (more visible than a small circle)
        using var brush = new SolidBrush( color );
        graphics.FillRectangle( brush, 2, 2, size - 4, size - 4 );

        // Add a white border for contrast
        using var pen = new System.Drawing.Pen( System.Drawing.Color.White, 2 );
        graphics.DrawRectangle( pen, 2, 2, size - 5, size - 5 );

        // Convert to WPF ImageSource
        var hBitmap = bitmap.GetHbitmap();

        try
        {
            var imageSource = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions() );

            imageSource.Freeze();

            return imageSource;
        }
        finally
        {
            DeleteObject( hBitmap );
        }
    }

    [System.Runtime.InteropServices.DllImport( "gdi32.dll" )]
    private static extern bool DeleteObject( IntPtr hObject );

    private void OnRequestAdded( object? sender, ApprovalRequest request )
    {
        Application.Current.Dispatcher.Invoke( () =>
        {
            // Update icon to pending state
            this.UpdateIcon();

            // Show balloon notification
            this._taskbarIcon?.ShowBalloonTip(
                "Approval Required",
                $"New command request pending approval\n{request.Command.Substring( 0, Math.Min( 50, request.Command.Length ) )}...",
                BalloonIcon.Warning );

            // Open a new approval window for this request
            this.OpenApprovalWindow( request );
        } );
    }

    private void OnQueueChanged( object? sender, EventArgs e )
    {
        Application.Current.Dispatcher.Invoke( this.UpdateIcon );
    }

    private void UpdateIcon()
    {
        if ( this._taskbarIcon == null )
        {
            return;
        }

        var hasPending = this._queue.HasPendingRequests;
        var isProcessing = this._processingCount > 0;

        ImageSource? newIcon;

        // Priority: pending (needs attention) > processing (auto-approved) > normal
        if ( hasPending )
        {
            newIcon = this._pendingIcon;
        }
        else if ( isProcessing )
        {
            newIcon = this._processingIcon;
        }
        else
        {
            newIcon = this._normalIcon;
        }

        // Force refresh by setting to null first (Hardcodet library quirk)
        var iconName = newIcon == this._normalIcon ? "Normal (Green)" :
                       newIcon == this._pendingIcon ? "Pending (Orange)" :
                       newIcon == this._processingIcon ? "Processing (Blue)" : "Unknown";

        System.Diagnostics.Debug.WriteLine( $"[TrayIcon] UpdateIcon: {iconName}, HasPending={hasPending}, IsProcessing={isProcessing}" );

        this._taskbarIcon.IconSource = null;
        this._taskbarIcon.IconSource = newIcon;

        var pendingCount = this._queue.PendingCount;

        if ( pendingCount > 0 )
        {
            this._taskbarIcon.ToolTipText = $"MCP Approval Server - {pendingCount} pending request(s)";
        }
        else if ( isProcessing )
        {
            this._taskbarIcon.ToolTipText = $"MCP Approval Server - Processing ({this._processingCount})";
        }
        else
        {
            this._taskbarIcon.ToolTipText = "MCP Approval Server - Ready";
        }
    }

    private void OnTrayLeftClick( object sender, RoutedEventArgs e )
    {
        this.OnShowOldestRequest( sender, e );
    }

    private void OnShowOldestRequest( object sender, RoutedEventArgs e )
    {
        var request = this._queue.GetOldestRequest();

        if ( request == null )
        {
            // No pending requests - show history window
            this.ShowHistoryWindow();

            return;
        }

        // Check if window is already open for this request
        if ( this._openWindows.TryGetValue( request.Id, out var existingWindow ) )
        {
            existingWindow.Activate();
            existingWindow.Focus();

            return;
        }

        this.OpenApprovalWindow( request );
    }

    private void ShowHistoryWindow()
    {
        // If history window is already open, just activate it
        if ( this._historyWindow != null && this._historyWindow.IsLoaded )
        {
            this._historyWindow.Activate();
            this._historyWindow.Focus();

            return;
        }

        // Create and show new history window
        var viewModel = new ViewModels.HistoryViewModel( this._historyService, this._queue );
        this._historyWindow = new HistoryWindow { DataContext = viewModel };

        this._historyWindow.Closed += ( s, e ) => this._historyWindow = null;

        this._historyWindow.Show();
        this._historyWindow.Activate();
    }

    private void OpenApprovalWindow( ApprovalRequest request )
    {
        // Check if window is already open for this request
        if ( this._openWindows.ContainsKey( request.Id ) )
        {
            this._openWindows[request.Id].Activate();

            return;
        }

        // Create and configure the approval window
        var viewModel = new ApprovalViewModel( request, this._queue );
        var window = new ApprovalWindow { DataContext = viewModel };

        // Track the window
        this._openWindows[request.Id] = window;

        window.Closed += ( s, e ) =>
        {
            this._openWindows.Remove( request.Id );
        };

        // Show the window
        window.Show();
        window.Activate();
    }

    private void OnExit( object sender, RoutedEventArgs e )
    {
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if ( this._disposed )
        {
            return;
        }

        this._disposed = true;

        this._queue.RequestAdded -= this.OnRequestAdded;
        this._queue.QueueChanged -= this.OnQueueChanged;

        // Close all open windows
        foreach ( var window in this._openWindows.Values )
        {
            window.Close();
        }

        this._openWindows.Clear();

        this._taskbarIcon?.Dispose();
        this._taskbarIcon = null;
    }
}
