// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PostSharp.Engineering.McpApprovalServer.Mcp.Services;
using PostSharp.Engineering.McpApprovalServer.Services;
using PostSharp.Engineering.McpApprovalServer.ViewModels;
using PostSharp.Engineering.McpApprovalServer.Views;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PostSharp.Engineering.McpApprovalServer;

/// <summary>
/// Main application class for the MCP Approval Server GUI.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private TrayIconService? _trayIconService;

    protected override async void OnStartup( StartupEventArgs e )
    {
        base.OnStartup( e );

        // Install global exception handlers so crashes are captured in the trace log
        // instead of silently terminating the tray app (at which point MCP sessions
        // would of course all be lost).
        AppDomain.CurrentDomain.UnhandledException += ( _, args ) =>
        {
            if ( args.ExceptionObject is Exception ex )
            {
                TraceLogger.Logger.Error(
                    $"AppDomain.UnhandledException (IsTerminating={args.IsTerminating})",
                    ex );
            }
            else
            {
                TraceLogger.Logger.Error(
                    $"AppDomain.UnhandledException (IsTerminating={args.IsTerminating}): {args.ExceptionObject ?? "<null>"}" );
            }
        };

        this.DispatcherUnhandledException += ( _, args ) =>
        {
            TraceLogger.Logger.Error( "Dispatcher.UnhandledException", args.Exception );

            // Don't let a UI exception tear the app down — MCP must stay up.
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += ( _, args ) =>
        {
            TraceLogger.Logger.Error( "TaskScheduler.UnobservedTaskException", args.Exception );
            args.SetObserved();
        };

        TraceLogger.Logger.Info( $"Application starting. Log file: {TraceLogger.Logger.LogFilePath}" );

        // Build and configure the host
        this._host = Host.CreateDefaultBuilder()
            .ConfigureServices( ( _, services ) =>
            {
                // Register services
                services.AddSingleton<ApprovalRequestQueue>();
                services.AddSingleton<CommandHistoryService>();
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<McpHttpServer>();

                // Register ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddTransient<ApprovalViewModel>();

                // Register Views
                services.AddSingleton<MainWindow>();
            } )
            .Build();

        // Start the host
        await this._host.StartAsync();

        // Get the tray icon service and initialize it
        this._trayIconService = this._host.Services.GetRequiredService<TrayIconService>();
        this._trayIconService.Initialize();

        // Start the MCP HTTP server
        var mcpServer = this._host.Services.GetRequiredService<McpHttpServer>();

        try
        {
            await mcpServer.StartAsync();
        }
        catch ( Exception ex )
        {
            MessageBox.Show(
                $"Failed to start MCP server: {ex.Message}\n\nThe server may already be running.",
                "MCP Approval Server",
                MessageBoxButton.OK,
                MessageBoxImage.Error );

            this.Shutdown( 1 );

            return;
        }

        // Show the main window (hidden, but required for WPF lifecycle)
        var mainWindow = this._host.Services.GetRequiredService<MainWindow>();
        mainWindow.Hide();
    }

    protected override async void OnExit( ExitEventArgs e )
    {
        if ( this._host != null )
        {
            // Stop the MCP server
            var mcpServer = this._host.Services.GetRequiredService<McpHttpServer>();
            await mcpServer.StopAsync();

            // Dispose the tray icon
            this._trayIconService?.Dispose();

            // Stop the host
            await this._host.StopAsync();
            this._host.Dispose();
        }

        base.OnExit( e );
    }
}