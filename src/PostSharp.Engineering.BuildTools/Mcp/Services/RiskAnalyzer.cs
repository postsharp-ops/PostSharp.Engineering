// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Mcp.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Mcp.Services;

/// <summary>
/// Analyzes command requests for risk using the Claude CLI.
/// </summary>
public sealed class RiskAnalyzer
{
    /// <summary>
    /// Guidance for common situations. This prompt section teaches the AI how to evaluate
    /// typical commands that come from Docker-contained Claude instances.
    /// </summary>
    private const string _situationGuidance = """
        ## Common Situations and Guidance

        ### Git Operations
        - `git push`: LOW risk if pushing to a feature (topic) branch, MEDIUM if pushing to main/master/develop
        - `git push --force`: HIGH risk - can destroy history, always flag for careful review
        - `git tag` / `git push --tags`: MEDIUM risk - tags are often used for releases
        - `git checkout` / `git switch`: LOW risk - just changing branches locally
        - `git reset --hard`: HIGH risk - can lose uncommitted work

        ### GitHub CLI (gh)
        - `gh pr create`: LOW risk - creating a PR is reversible and requires human merge
        - `gh pr view`: LOW risk - even on private repos
        - `gh pr merge`: MEDIUM risk - merging changes the target branch
        - `gh release create`: MEDIUM risk - creates a public release
        - `gh issue create/comment`: LOW risk - creating issues is low impact
        - `gh issue close`: MEDIUM risk - can be reponed
        - `gh issue delete`: HIGH risk - can be reponed
        - `gh repo delete`: CRITICAL risk - never approve without explicit confirmation

        ### Build/Package Operations
        - `dotnet build` / `dotnet test` / `dotnet pack` : LOW risk - local operations (but should normally be done in the container)
        - `dotnet nuget push`: HIGH risk - publishes packages publicly, hard to undo
        
        ### File Operations
        - Commands that delete files outside the working directory: HIGH risk
        - Commands that modify system files: CRITICAL risk
        - Commands confined to the project directory: generally LOW risk

        ### Red Flags (always flag as HIGH/CRITICAL)
        - Commands with encoded/obfuscated content (base64, hex strings)
        - Commands that download and execute scripts (curl | bash, iex, etc.)
        - Commands targeting paths outside the project (especially system paths)
        - Commands that disable security features or skip verification
        - Sequences that seem to be probing or gathering information before a bigger action
        - Mismatches between claimed purpose and actual command behavior

        ### Environment variables
        - Read ANY environment variable: HIGH risk, expose secrets
        - Write ANY environment variable: HIGH risk
        """;

    // Suppress CA1822 - this is a DI service, keeping as instance method for consistency
#pragma warning disable CA1822
    public async Task<RiskAssessment> AnalyzeAsync(
        string command,
        string claimedPurpose,
        string workingDirectory,
        IReadOnlyList<CommandRecord> history,
        CancellationToken cancellationToken = default )
#pragma warning restore CA1822
    {
        var prompt = BuildAnalysisPrompt( command, claimedPurpose, workingDirectory, history );

        try
        {
            using var timeoutCts = new CancellationTokenSource( TimeSpan.FromSeconds( 60 ) );
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken, timeoutCts.Token );

            var startInfo = new ProcessStartInfo
            {
                FileName = "claude",
                Arguments = $"-p \"{EscapeForShell( prompt )}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start( startInfo );

            if ( process == null )
            {
                return RiskAssessment.Default( "Failed to start Claude CLI for analysis" );
            }

            var output = await process.StandardOutput.ReadToEndAsync( linkedCts.Token );
            await process.WaitForExitAsync( linkedCts.Token );

            if ( process.ExitCode != 0 )
            {
                return RiskAssessment.Default( "Claude CLI analysis failed" );
            }

            return RiskAssessment.Parse( output );
        }
        catch ( OperationCanceledException )
        {
            return RiskAssessment.Default( "Analysis timed out - human review required" );
        }
        catch ( Exception ex )
        {
            return RiskAssessment.Default( $"Analysis error: {ex.Message}" );
        }
    }

    private static string BuildAnalysisPrompt(
        string command,
        string claimedPurpose,
        string workingDirectory,
        IReadOnlyList<CommandRecord> history )
    {
        var sb = new StringBuilder();

        // System context
        sb.AppendLine( "You are a security reviewer analyzing command requests from a sandboxed Claude instance running in Docker." );
        sb.AppendLine( "The sandboxed instance needs to execute commands on the host machine and requires human approval." );
        sb.AppendLine( "Your job is to assess the risk and provide a recommendation to help the human make an informed decision." );
        sb.AppendLine();

        // Situation guidance
        sb.AppendLine( _situationGuidance );
        sb.AppendLine();

        // Current request
        sb.AppendLine( "## Current Request" );
        sb.AppendLine();
        sb.Append( CultureInfo.InvariantCulture, $"**Command:** `{command}`" ).AppendLine();
        sb.Append( CultureInfo.InvariantCulture, $"**Claimed purpose:** {claimedPurpose}" ).AppendLine();
        sb.Append( CultureInfo.InvariantCulture, $"**Working directory:** {workingDirectory}" ).AppendLine();
        sb.AppendLine();

        // Session history
        if ( history.Count > 0 )
        {
            sb.AppendLine( "## Session History (recent commands)" );
            sb.AppendLine();

            foreach ( var record in history.TakeLast( 10 ) )
            {
                var status = record.Approved ? "APPROVED" : "REJECTED";
                sb.Append( CultureInfo.InvariantCulture, $"- [{status}] `{record.Command}`" ).AppendLine();
                sb.Append( CultureInfo.InvariantCulture, $"  Purpose: {record.ClaimedPurpose}" ).AppendLine();
            }

            sb.AppendLine();
        }

        // Evaluation criteria
        sb.AppendLine( "## Your Task" );
        sb.AppendLine();
        sb.AppendLine( "Evaluate:" );
        sb.AppendLine( "1. Does the command match the claimed purpose?" );
        sb.AppendLine( "2. Is there anything suspicious in the command or the sequence of commands?" );
        sb.AppendLine( "3. What is the blast radius if this goes wrong?" );
        sb.AppendLine( "4. Is this a reasonable request given the context?" );
        sb.AppendLine();

        // Response format
        sb.AppendLine( "## Response Format" );
        sb.AppendLine();
        sb.AppendLine( "Respond with EXACTLY these three lines (no additional text):" );
        sb.AppendLine( "```" );
        sb.AppendLine( "RISK: LOW|MEDIUM|HIGH|CRITICAL" );
        sb.AppendLine( "RECOMMEND: APPROVE|REJECT" );
        sb.AppendLine( "REASON: <one concise sentence explaining your assessment>" );
        sb.AppendLine( "```" );

        return sb.ToString();
    }

    private static string EscapeForShell( string input )
    {
        // Escape double quotes and backslashes for shell
        return input
            .Replace( "\\", "\\\\", StringComparison.Ordinal )
            .Replace( "\"", "\\\"", StringComparison.Ordinal )
            .Replace( "\n", "\\n", StringComparison.Ordinal )
            .Replace( "\r", "", StringComparison.Ordinal );
    }
}
