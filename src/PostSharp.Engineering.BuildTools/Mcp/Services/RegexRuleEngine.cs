// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Mcp.Models;
using PostSharp.Engineering.BuildTools.Utilities;
using Spectre.Console;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Mcp.Services;

/// <summary>
/// Evaluates commands against regex-based rules with contextual awareness (git branch, etc.).
/// </summary>
public sealed class RegexRuleEngine
{
    private readonly ConsoleHelper _console;

    public RegexRuleEngine( ConsoleHelper console )
    {
        this._console = console;
    }

    /// <summary>
    /// Evaluates a command against regex-based rules, considering git context.
    /// </summary>
    public Task<RiskAssessment> EvaluateAsync(
        string command,
        string claimedPurpose,
        string workingDirectory,
        IReadOnlyList<CommandRecord> sessionHistory,
        CancellationToken cancellationToken = default )
    {
        // Build command context with git information
        var context = BuildContext( command, workingDirectory );

        // Evaluate all rules in order
        foreach ( var rule in CommandRules.DefaultRules )
        {
            // Check if pattern matches
            if ( !rule.Pattern.IsMatch( command ) )
            {
                continue;
            }

            // Check if condition is satisfied (if present)
            if ( rule.Condition != null && !rule.Condition( context ) )
            {
                continue;
            }

            // Rule matched! Log and return assessment
            AnsiConsole.MarkupLine( $"[yellow][[Regex Rule Matched]][/] {Markup.Escape( rule.Name )}: {Markup.Escape( rule.Reason )} ({rule.RiskLevel}/{rule.Recommendation})" );

            return Task.FromResult( new RiskAssessment
            {
                Level = rule.RiskLevel,
                Recommendation = rule.Recommendation,
                Reason = rule.Reason,
                RuleName = rule.Name,
                IsAgnostic = rule.IsAgnostic
            } );
        }

        // No rules matched - return default LOW/APPROVE
        return Task.FromResult( new RiskAssessment
        {
            Level = RiskLevel.Low,
            Recommendation = Recommendation.Approve,
            Reason = "No regex rules matched - command appears safe",
            RuleName = null
        } );
    }

    private CommandContext BuildContext( string command, string workingDirectory )
    {
        var context = new CommandContext
        {
            Command = command,
            WorkingDirectory = workingDirectory
        };

        // Only gather git context if working directory exists and is a git repository
        if ( !Directory.Exists( workingDirectory ) )
        {
            return context;
        }

        var gitDir = Path.Combine( workingDirectory, ".git" );

        if ( !Directory.Exists( gitDir ) && !File.Exists( gitDir ) )
        {
            return context;
        }

        // Try to get current branch
        if ( GitHelper.TryGetCurrentBranch( this._console, workingDirectory, out var currentBranch ) )
        {
            context = context with { CurrentBranch = currentBranch };
        }

        // Try to get remote URL (using git config command directly)
        if ( TryGetRemoteUrl( workingDirectory, "origin", out var remoteUrl ) )
        {
            context = context with { RemoteUrl = remoteUrl };
        }

        // Try to check if merge is in progress (using git rev-parse command directly)
        if ( TryGetIsMergeInProgress( workingDirectory, out var isMerging ) )
        {
            context = context with { IsMergeInProgress = isMerging };
        }

        return context;
    }

    private bool TryGetRemoteUrl( string repoDirectory, string remoteName, out string? url )
    {
        if ( !ToolInvocationHelper.InvokeTool(
                 this._console,
                 "git",
                 $"config --get remote.{remoteName}.url",
                 repoDirectory,
                 out var exitCode,
                 out var output,
                 ToolInvocationOptions.Default with { Silent = true } )
             || exitCode != 0 )
        {
            url = null;

            return false;
        }

        url = output.Trim();

        return true;
    }

    private bool TryGetIsMergeInProgress( string repoDirectory, out bool isMergeInProgress )
    {
        if ( !ToolInvocationHelper.InvokeTool(
                 this._console,
                 "git",
                 "rev-parse -q --verify MERGE_HEAD",
                 repoDirectory,
                 out var exitCode,
                 out var output,
                 ToolInvocationOptions.Default with { Silent = true } )
             || exitCode != 0 )
        {
            isMergeInProgress = false;

            // Exit code 1 with no output means that a merge is not in progress
            return exitCode == 1 && output == "";
        }

        // Exit code 0 means that merge is in progress
        isMergeInProgress = true;

        return true;
    }
}
