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
    private static readonly SemaphoreSlim _approvalLock = new( 1, 1 );

    private readonly CommandHistoryService _history;
    private readonly RiskAnalyzer _analyzer;
    private readonly RegexRuleEngine _regexEngine;
    private readonly ApprovalPrompter _prompter;
    private readonly CommandExecutor _executor;

    public ExecuteCommandTool(
        CommandHistoryService history,
        RiskAnalyzer analyzer,
        RegexRuleEngine regexEngine,
        ApprovalPrompter prompter,
        CommandExecutor executor )
    {
        this._history = history;
        this._analyzer = analyzer;
        this._regexEngine = regexEngine;
        this._prompter = prompter;
        this._executor = executor;
    }

    [McpServerTool]
    [Description( "Execute a PowerShell command on the host machine. Requires human approval. Use this for git push, GitHub operations, and other actions that affect external systems or require privileges or tokens that the container does not have." )]
    public async Task<CommandResult> ExecuteCommand(
        [Description( "The command to execute (e.g., 'git push origin main'). Must be valid PowerShell script." )]
        string command,
        [Description( "The working directory for command execution" )]
        string workingDirectory,
        [Description( "A clear explanation of why this command is needed" )]
        string claimedPurpose,
        CancellationToken cancellationToken = default )
    {
        // Use a constant session ID for single session model
        const string sessionId = "default";

        // Check if another approval request is already in progress
        if ( !await _approvalLock.WaitAsync( 0, cancellationToken ) )
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine( "[red]Another approval request is being processed. Wait until this request is approved.[/]" );
            AnsiConsole.WriteLine();

            return CommandResult.Error( "Another approval request is being processed. Wait until this request is approved." );
        }

        try
        {
            // Log incoming request
            AnsiConsole.WriteLine();
            AnsiConsole.Write( new Rule( "[yellow]Incoming Command Request[/]" ) );
            AnsiConsole.MarkupLine( $"[dim]Time:[/] {DateTime.Now:yyyy-MM-dd HH:mm:ss}" );
            AnsiConsole.MarkupLine( $"[dim]Command:[/] [white]{command.EscapeMarkup()}[/]" );
            AnsiConsole.MarkupLine( $"[dim]Working Directory:[/] {workingDirectory.EscapeMarkup()}" );
            AnsiConsole.MarkupLine( $"[dim]Purpose:[/] {claimedPurpose.EscapeMarkup()}" );
            AnsiConsole.WriteLine();

            try
            {
            // 1. Get session history
            var sessionHistory = this._history.GetHistory( sessionId );

            // 2. Risk analysis - run both AI and Regex analyzers in parallel
            var aiTask = this._analyzer.AnalyzeAsync(
                command,
                claimedPurpose,
                workingDirectory,
                sessionHistory,
                cancellationToken );

            var regexTask = this._regexEngine.EvaluateAsync(
                command,
                claimedPurpose,
                workingDirectory,
                sessionHistory,
                cancellationToken );

            var assessments = await Task.WhenAll( aiTask, regexTask );
            var aiAssessment = assessments[0];
            var regexAssessment = assessments[1];

            // 3. Combine assessments (take maximum risk)
            var assessment = RiskCombiner.Combine( aiAssessment, regexAssessment );

            // 4. Prompt user for approval (pass both assessments for display)
            var approved = await this._prompter.RequestApprovalAsync(
                command,
                claimedPurpose,
                workingDirectory,
                assessment,
                aiAssessment,
                regexAssessment );

            // 5. Execute if approved
            CommandResult result;

            if ( approved )
            {
                AnsiConsole.Write( new Rule( "[green]Request Approved[/]" ) );
                AnsiConsole.WriteLine();
                AnsiConsole.Write( new Rule( "[blue]Executing Request[/]" ) );
                AnsiConsole.WriteLine();

                result = await this._executor.ExecuteAsync( command, workingDirectory, cancellationToken );

                AnsiConsole.WriteLine();
                AnsiConsole.Write( new Rule( "[blue]Request Completed[/]" ) );
                AnsiConsole.WriteLine();
            }
            else
            {
                AnsiConsole.Write( new Rule( "[red]Request Rejected[/]" ) );
                AnsiConsole.WriteLine();

                result = CommandResult.Rejected();
            }

            // 6. Record in history
            this._history.Record( sessionId, command, claimedPurpose, approved, result );

            return result;
            }
            catch ( Exception ex )
            {
                AnsiConsole.MarkupLine( $"[red]Error: {ex.Message.EscapeMarkup()}[/]" );
                AnsiConsole.WriteException( ex );

                return CommandResult.Error( ex.Message );
            }
        }
        finally
        {
            // Always release the lock when done
            _approvalLock.Release();
        }
    }
}
