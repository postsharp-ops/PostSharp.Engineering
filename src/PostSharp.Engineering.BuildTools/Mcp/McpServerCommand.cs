// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PostSharp.Engineering.BuildTools.Mcp.Services;
using PostSharp.Engineering.BuildTools.Mcp.Tools;
using PostSharp.Engineering.BuildTools.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Mcp;

/// <summary>
/// Command that starts the MCP approval server.
/// This server enables Docker-contained Claude instances to request host-level actions
/// through a secure, human-in-the-loop approval workflow.
/// </summary>
public sealed class McpServerCommand : AsyncCommand<McpServerCommandSettings>
{
    public override async Task<int> ExecuteAsync( CommandContext context, McpServerCommandSettings settings )
    {
        try
        {
            AnsiConsole.Write( new Rule( "[cyan]MCP Approval Server[/]" ) );
            AnsiConsole.WriteLine();

            var builder = WebApplication.CreateBuilder();

            // Register services for tool dependencies
            builder.Services.AddSingleton<CommandHistoryService>();
            builder.Services.AddSingleton<RiskAnalyzer>();
            builder.Services.AddSingleton<RegexRuleEngine>();
            builder.Services.AddSingleton<ApprovalPrompter>();
            builder.Services.AddSingleton<CommandExecutor>();

            // Register ConsoleHelper for GitHelper usage
            builder.Services.AddSingleton( _ => new ConsoleHelper() );

            // Register the tool itself
            builder.Services.AddScoped<ExecuteCommandTool>();

            // Configure MCP server
            builder.Services
                .AddMcpServer()
                .WithHttpTransport()
                .WithToolsFromAssembly( typeof(McpServerCommand).Assembly );

            // Configure port (0 = dynamic)
            // Bind to 0.0.0.0 so Docker containers can connect via host.docker.internal
            // Note: Must use explicit IP (not localhost) for dynamic port binding
            var port = settings.Port;
            builder.WebHost.UseUrls( $"http://0.0.0.0:{port}" );

            // Enable logging to console for debugging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel( LogLevel.Warning );

            var app = builder.Build();

            // Add request logging middleware (only if verbose mode is enabled)
            if ( settings.Verbose )
            {
                app.Use( async ( httpContext, next ) =>
                {
                    AnsiConsole.MarkupLine( $"[dim]HTTP {httpContext.Request.Method} {httpContext.Request.Path}[/]" );

                    await next();
                } );
            }

            // Map MCP endpoints with token as base path (if configured)
            if ( !string.IsNullOrEmpty( settings.Secret ) )
            {
                app.MapMcp( $"/{settings.Secret}" );
            }
            else
            {
                app.MapMcp();
            }

            // Start the server
            await app.StartAsync();

            // Get the actual port (important for dynamic assignment)
            var addresses = app.Urls;
            var actualPort = new Uri( addresses.First() ).Port;

            // Write port to file for DockerBuild.ps1 to read
            if ( !string.IsNullOrEmpty( settings.PortFile ) )
            {
                await File.WriteAllTextAsync( settings.PortFile, actualPort.ToString( CultureInfo.InvariantCulture ) );
                AnsiConsole.MarkupLine( $"[dim]Port written to: {settings.PortFile}[/]" );
            }

            AnsiConsole.MarkupLine( $"[green]Server listening on port {actualPort}[/]" );
            AnsiConsole.MarkupLine( "[dim]Press Ctrl+C to stop the server[/]" );
            AnsiConsole.WriteLine();

            // Wait for shutdown signal
            await app.WaitForShutdownAsync();

            AnsiConsole.MarkupLine( "[yellow]Server shutting down...[/]" );

            return 0;
        }
        catch ( Exception ex )
        {
            AnsiConsole.WriteException( ex );

            return 1;
        }
    }
}