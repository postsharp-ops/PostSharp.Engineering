// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.McpApprovalServer.Mcp.Models;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.McpApprovalServer.Mcp.Services;

/// <summary>
/// Evaluates commands against regex-based rules with contextual awareness (git branch, etc.).
/// </summary>
public sealed class RegexRuleEngine
{
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
        var context = this.BuildContext( command, workingDirectory );

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

            // Rule matched! Return assessment
            return Task.FromResult(
                new RiskAssessment { Level = rule.RiskLevel, Recommendation = rule.Recommendation, Reason = rule.Reason, RuleName = rule.Name } );
        }

        // No rules matched - return agnostic (defer to AI analysis)
        return Task.FromResult(
            new RiskAssessment
            {
                Level = RiskLevel.Low, Recommendation = Recommendation.None, Reason = "No regex rules matched - deferring to AI analysis", RuleName = null
            } );
    }

    private CommandContext BuildContext( string command, string workingDirectory )
    {
        var context = new CommandContext { Command = command, WorkingDirectory = workingDirectory };

        // Skip git context if directory doesn't exist
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
        if ( this.TryRunGitCommand( workingDirectory, "rev-parse --abbrev-ref HEAD", out var currentBranch ) )
        {
            context = context with { CurrentBranch = currentBranch.Trim() };
        }

        // Try to get remote URL
        if ( this.TryRunGitCommand( workingDirectory, "config --get remote.origin.url", out var remoteUrl ) )
        {
            context = context with { RemoteUrl = remoteUrl.Trim() };
        }

        // Try to check if merge is in progress
        if ( this.TryRunGitCommand( workingDirectory, "rev-parse -q --verify MERGE_HEAD", out _ ) )
        {
            context = context with { IsMergeInProgress = true };
        }

        return context;
    }

    private bool TryRunGitCommand( string workingDirectory, string arguments, out string output )
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start( startInfo );

            if ( process == null )
            {
                output = string.Empty;

                return false;
            }

            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0;
        }
        catch
        {
            output = string.Empty;

            return false;
        }
    }
}
