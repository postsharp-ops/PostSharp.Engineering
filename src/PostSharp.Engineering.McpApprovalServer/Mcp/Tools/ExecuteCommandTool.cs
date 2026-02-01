// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using ModelContextProtocol.Server;
using PostSharp.Engineering.McpApprovalServer.Mcp.Models;
using PostSharp.Engineering.McpApprovalServer.Mcp.Services;
using PostSharp.Engineering.McpApprovalServer.Services;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.McpApprovalServer.Mcp.Tools;

/// <summary>
/// MCP tool that executes commands on the host machine with human approval.
/// Supports concurrent approval requests when used with a GUI-based approval prompter.
/// </summary>
[McpServerToolType]
public sealed class ExecuteCommandTool
{
    private readonly CommandHistoryService _history;
    private readonly RiskAnalyzer _analyzer;
    private readonly RegexRuleEngine _regexEngine;
    private readonly IApprovalPrompter _prompter;
    private readonly CommandExecutor _executor;
    private readonly ICommandLogger? _logger;

    public ExecuteCommandTool(
        CommandHistoryService history,
        RiskAnalyzer analyzer,
        RegexRuleEngine regexEngine,
        IApprovalPrompter prompter,
        CommandExecutor executor,
        ICommandLogger? logger = null )
    {
        this._history = history;
        this._analyzer = analyzer;
        this._regexEngine = regexEngine;
        this._prompter = prompter;
        this._executor = executor;
        this._logger = logger;
    }

    [McpServerTool]
    [Description(
        "Execute a PowerShell command on the host machine. Requires human approval. Use this for git push, GitHub operations, and other actions that affect external systems or require privileges or tokens that the container does not have." )]
    public async Task<CommandResult> ExecuteCommand(
        [Description( "The command to execute (e.g., 'git push origin main'). Must be valid PowerShell script." )] string command,
        [Description( "The working directory for command execution" )]
        string workingDirectory,
        [Description( "A clear explanation of why this command is needed" )]
        string claimedPurpose,
        CancellationToken cancellationToken = default )
    {
        // Use a constant session ID for single session model
        const string sessionId = "default";
        var branch = await GitHelper.GetBranchAsync( workingDirectory, cancellationToken );

        // Log incoming request
        this._logger?.LogSection( "Incoming Command Request" );
        this._logger?.LogInfo( $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}" );
        this._logger?.LogInfo( $"Command: {command}" );
        this._logger?.LogInfo( $"Working Directory: {workingDirectory}" );
        this._logger?.LogInfo( $"Purpose: {claimedPurpose}" );
        this._logger?.LogInfo( $"Git Branch: {branch}" );
        
        try
        {
            // 1. Check if this exact command was previously approved
            if ( this._history.WasPreviouslyApproved( sessionId, command, workingDirectory ) )
            {
                this._logger?.LogSuccess( "Auto-approved (previously approved)" );
                this._logger?.LogSection( "Executing Request" );

                var autoApprovedResult = await this._executor.ExecuteAsync( command, workingDirectory, cancellationToken );

                this._logger?.LogSection( "Request Completed" );

                // Record in history
                this._history.Record( sessionId, command, workingDirectory, branch, claimedPurpose, approved: true, autoApprovedResult );

                return autoApprovedResult;
            }

            // 2. Get session history
            var sessionHistory = this._history.GetHistory( sessionId );

            // 3. Risk analysis - run both AI and Regex analyzers in parallel
            var aiTask = this._analyzer.AnalyzeAsync(
                command,
                claimedPurpose,
                workingDirectory,
                sessionHistory,
                cancellationToken );

            var regexTask = RegexRuleEngine.EvaluateAsync(
                command,
                claimedPurpose,
                workingDirectory,
                sessionHistory,
                cancellationToken );

            var assessments = await Task.WhenAll( aiTask, regexTask );
            var aiAssessment = assessments[0];
            var regexAssessment = assessments[1];

            // 4. Combine assessments (take maximum risk)
            var assessment = RiskCombiner.Combine( aiAssessment, regexAssessment );

            // 5. Prompt user for approval (pass both assessments for display)
            var approved = await this._prompter.RequestApprovalAsync(
                command,
                claimedPurpose,
                workingDirectory,
                assessment,
                aiAssessment,
                regexAssessment,
                cancellationToken );

            // 6. Execute if approved
            CommandResult result;

            if ( approved )
            {
                this._logger?.LogSection( "Request Approved" );
                this._logger?.LogSection( "Executing Request" );

                result = await this._executor.ExecuteAsync( command, workingDirectory, cancellationToken );

                this._logger?.LogSection( "Request Completed" );
            }
            else
            {
                this._logger?.LogSection( "Request Rejected" );

                result = CommandResult.Rejected();
            }

            // 7. Record in history
            this._history.Record( sessionId, command, workingDirectory,  branch, claimedPurpose, approved, result );

            return result;
        }
        catch ( Exception ex )
        {
            this._logger?.LogError( $"Error: {ex.Message}" );

            return CommandResult.Error( ex.Message );
        }
    }
}