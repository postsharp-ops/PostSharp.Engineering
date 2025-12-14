// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using ModelContextProtocol.Server;
using PostSharp.Engineering.BuildTools.Mcp.Models;
using PostSharp.Engineering.BuildTools.Mcp.Services;
using Spectre.Console;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Mcp.Tools;

/// <summary>
/// MCP tool that executes commands on the host machine with human approval.
/// </summary>
[McpServerToolType]
public sealed class ExecuteCommandTool
{
    private readonly CommandHistoryService _history;
    private readonly RiskAnalyzer _analyzer;
    private readonly ApprovalPrompter _prompter;
    private readonly CommandExecutor _executor;

    public ExecuteCommandTool(
        CommandHistoryService history,
        RiskAnalyzer analyzer,
        ApprovalPrompter prompter,
        CommandExecutor executor )
    {
        this._history = history;
        this._analyzer = analyzer;
        this._prompter = prompter;
        this._executor = executor;
    }

    [McpServerTool]
    [Description( "Execute a command on the host machine. Requires human approval. Use this for git push, GitHub operations, and other actions that affect external systems." )]
    public async Task<CommandResult> ExecuteCommand(
        [Description( "Unique session identifier for tracking command history" )]
        string sessionId,
        [Description( "The command to execute (e.g., 'git push origin main')" )]
        string command,
        [Description( "The working directory for command execution" )]
        string workingDirectory,
        [Description( "A clear explanation of why this command is needed" )]
        string claimedPurpose,
        CancellationToken cancellationToken = default )
    {
        // Log incoming request
        AnsiConsole.WriteLine();
        AnsiConsole.Write( new Rule( "[yellow]Incoming Command Request[/]" ) );
        AnsiConsole.MarkupLine( $"[dim]Time:[/] {DateTime.Now:yyyy-MM-dd HH:mm:ss}" );
        AnsiConsole.MarkupLine( $"[dim]Session:[/] {sessionId}" );
        AnsiConsole.MarkupLine( $"[dim]Command:[/] [white]{command.EscapeMarkup()}[/]" );
        AnsiConsole.MarkupLine( $"[dim]Working Directory:[/] {workingDirectory.EscapeMarkup()}" );
        AnsiConsole.MarkupLine( $"[dim]Purpose:[/] {claimedPurpose.EscapeMarkup()}" );
        AnsiConsole.WriteLine();

        try
        {
            // 1. Get session history
            var sessionHistory = this._history.GetHistory( sessionId );

            // 2. Risk analysis
            var assessment = await this._analyzer.AnalyzeAsync(
                command,
                claimedPurpose,
                workingDirectory,
                sessionHistory,
                cancellationToken );

            // 3. Prompt user for approval
            var approved = await this._prompter.RequestApprovalAsync(
                command,
                claimedPurpose,
                assessment );

            // 4. Execute if approved
            CommandResult result;

            if ( approved )
            {
                result = await this._executor.ExecuteAsync( command, workingDirectory, cancellationToken );
            }
            else
            {
                result = CommandResult.Rejected( assessment.Reason );
            }

            // 5. Record in history
            this._history.Record( sessionId, command, claimedPurpose, approved, result );

            AnsiConsole.Write( new Rule( $"[{( approved ? "green" : "red" )}]Request {( approved ? "Approved" : "Rejected" )}[/]" ) );
            AnsiConsole.WriteLine();

            return result;
        }
        catch ( Exception ex )
        {
            AnsiConsole.MarkupLine( $"[red]Error: {ex.Message.EscapeMarkup()}[/]" );
            AnsiConsole.WriteException( ex );

            return CommandResult.Error( ex.Message );
        }
    }
}
