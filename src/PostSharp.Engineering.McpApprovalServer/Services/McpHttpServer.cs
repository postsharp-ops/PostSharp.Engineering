// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using PostSharp.Engineering.McpApprovalServer.Mcp.Services;
using PostSharp.Engineering.McpApprovalServer.Mcp.Tools;
using System;
using System.Threading.Tasks;

namespace PostSharp.Engineering.McpApprovalServer.Services;

/// <summary>
/// Hosts the MCP server on a fixed port using ASP.NET Core Kestrel.
/// Binds to all interfaces to allow Docker container access via host gateway.
/// </summary>
public sealed class McpHttpServer
{
    /// <summary>
    /// The fixed port number for the MCP approval server.
    /// </summary>
    public const int FixedPort = 9847;

    private readonly ApprovalRequestQueue _queue;
    private readonly TrayIconService _trayIconService;
    private readonly CommandHistoryService _historyService;
    private WebApplication? _app;

    public McpHttpServer( ApprovalRequestQueue queue, TrayIconService trayIconService, CommandHistoryService historyService )
    {
        this._queue = queue;
        this._trayIconService = trayIconService;
        this._historyService = historyService;
    }

    /// <summary>
    /// Gets whether the server is currently running.
    /// </summary>
    public bool IsRunning => this._app != null;

    /// <summary>
    /// Starts the MCP HTTP server on the fixed port.
    /// </summary>
    public async Task StartAsync()
    {
        TraceLogger.Logger.Info( $"McpHttpServer.StartAsync: Starting on http://0.0.0.0:{FixedPort}" );

        var builder = WebApplication.CreateBuilder();

        // Bind to all interfaces so Docker containers can access via host gateway IP
        builder.WebHost.UseUrls( $"http://0.0.0.0:{FixedPort}" );

        // Configure Kestrel for long-lived SSE connections
        builder.WebHost.ConfigureKestrel( options =>
        {
            options.Limits.KeepAliveTimeout = TimeSpan.MaxValue;
            options.Limits.RequestHeadersTimeout = TimeSpan.MaxValue;
            options.Limits.MinRequestBodyDataRate = null;
            options.Limits.MinResponseDataRate = null;
        } );

        // Disable the MCP server's per-session idle timeout so long Claude
        // sessions (hours or days) don't get their server-side session
        // disposed while the user is idle. Without this, the SDK defaults
        // to 2 hours, after which a reconnect is required.
        builder.Services.Configure<HttpServerTransportOptions>( options =>
        {
            options.IdleTimeout = TimeSpan.MaxValue;
            options.MaxIdleSessionCount = int.MaxValue;
        } );

        // Register services for tool dependencies (use shared instances from main DI container)
        builder.Services.AddSingleton( this._historyService );
        builder.Services.AddSingleton<RiskAnalyzer>();
        builder.Services.AddSingleton<RegexRuleEngine>();
        builder.Services.AddSingleton<CommandExecutor>();

        // Register our GUI-based approval prompter
        builder.Services.AddSingleton( this._queue );
        builder.Services.AddSingleton( this._trayIconService );
        builder.Services.AddSingleton<IApprovalPrompter, GuiApprovalPrompter>();

        // Register the tool itself
        builder.Services.AddScoped<ExecuteCommandTool>();

        // Configure MCP server
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly( typeof(ExecuteCommandTool).Assembly );

        // Configure logging — route framework logs (Kestrel, MCP SDK, ASP.NET) to
        // the TraceLogger file so drops and errors are diagnosable after the fact.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddProvider( new TraceLoggerProvider() );
        builder.Logging.SetMinimumLevel( LogLevel.Information );

        // Silence the noisiest Microsoft categories but keep MCP/Kestrel warnings+.
        builder.Logging.AddFilter( "Microsoft.AspNetCore", LogLevel.Warning );
        builder.Logging.AddFilter( "Microsoft.Hosting", LogLevel.Warning );
        builder.Logging.AddFilter( "ModelContextProtocol", LogLevel.Information );

        this._app = builder.Build();

        // Add health endpoint for DockerBuild.ps1 to detect running server
        this._app.MapGet( "/health", () => "OK" );

        // Map MCP endpoints (no authentication - localhost only)
        this._app.MapMcp();

        // Start the server
        await this._app.StartAsync();

        TraceLogger.Logger.Info( $"McpHttpServer.StartAsync: Started successfully. Log file: {TraceLogger.Logger.LogFilePath}" );
    }

    /// <summary>
    /// Stops the MCP HTTP server.
    /// </summary>
    public async Task StopAsync()
    {
        if ( this._app != null )
        {
            TraceLogger.Logger.Info( "McpHttpServer.StopAsync: Stopping." );
            await this._app.StopAsync();
            await this._app.DisposeAsync();
            this._app = null;
            TraceLogger.Logger.Info( "McpHttpServer.StopAsync: Stopped." );
        }
    }
}
