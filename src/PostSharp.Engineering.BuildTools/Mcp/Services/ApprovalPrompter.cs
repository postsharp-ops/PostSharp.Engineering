// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Mcp.Models;
using Spectre.Console;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Mcp.Services;

/// <summary>
/// Prompts the user for approval of command execution using Spectre.Console.
/// </summary>
public sealed class ApprovalPrompter
{
    private const string _normalTitle = "MCP Approval Server";
    private const string _alertTitle = "⚠️ APPROVAL NEEDED ⚠️";

    // Suppress CA1822 - this is a DI service, keeping as instance method for consistency
#pragma warning disable CA1822
    public Task<bool> RequestApprovalAsync(
        string command,
        string claimedPurpose,
        string workingDirectory,
        RiskAssessment combinedAssessment,
        RiskAssessment aiAssessment,
        RiskAssessment regexAssessment )
#pragma warning restore CA1822
    {
        // Auto-approve LOW risk commands when combined assessment recommends approval
        if ( combinedAssessment.Level == RiskLevel.Low && combinedAssessment.Recommendation == Recommendation.Approve )
        {
            // Pleasant single beep for auto-approve
            try
            {
#pragma warning disable CA1416 // Platform compatibility - we handle non-Windows in catch
                Console.Beep( 1200, 150 );
#pragma warning restore CA1416
            }
            catch
            {
                // Beep may not be supported on all systems
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine( "[green]Auto-approved (LOW risk)[/]" );
            AnsiConsole.MarkupLine( $"[dim]Reason: {Markup.Escape( combinedAssessment.Reason )}[/]" );
            AnsiConsole.WriteLine();

            return Task.FromResult( true );
        }

        // HIGH/CRITICAL risk commands always require manual approval (no auto-reject)

        // Alert beep for user approval required
        try
        {
#pragma warning disable CA1416 // Platform compatibility - we handle non-Windows in catch
            Console.Beep( 800, 200 );
            Console.Beep( 1000, 200 );
#pragma warning restore CA1416
        }
        catch
        {
            // Beep may not be supported on all systems
        }

        // Start title blinking in background
        using var blinkCts = new CancellationTokenSource();
        var blinkTask = BlinkTitleAsync( blinkCts.Token );

        try
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write( new Rule( "[yellow]Command Approval Request[/]" ) );
            AnsiConsole.WriteLine();

            var table = new Table();
            table.AddColumn( "Field" );
            table.AddColumn( "Value" );
            table.Border( TableBorder.Rounded );

            table.AddRow( "[bold]Command[/]", $"[white]{Markup.Escape( command )}[/]" );
            table.AddRow( "[bold]Working Directory[/]", $"[blue]{Markup.Escape( workingDirectory )}[/]" );
            table.AddRow( "[bold]Purpose[/]", $"[dim]{Markup.Escape( claimedPurpose )}[/]" );

            // AI Assessment section
            table.AddRow( "", "" ); // Empty row for spacing
            table.AddRow( "[bold yellow]AI Assessment[/]", "" );
            table.AddRow( "  Risk Level", GetRiskMarkup( aiAssessment.Level ) );
            table.AddRow( "  Recommendation", GetRecommendationMarkup( aiAssessment.Recommendation ) );
            table.AddRow( "  Reason", $"[dim]{Markup.Escape( aiAssessment.Reason )}[/]" );

            // Regex Assessment section
            table.AddRow( "", "" ); // Empty row for spacing
            table.AddRow( "[bold cyan]Regex Assessment[/]", "" );
            table.AddRow( "  Risk Level", GetRiskMarkup( regexAssessment.Level ) );
            table.AddRow( "  Recommendation", GetRecommendationMarkup( regexAssessment.Recommendation ) );
            table.AddRow( "  Reason", $"[dim]{Markup.Escape( regexAssessment.Reason )}[/]" );

            if ( regexAssessment.RuleName != null )
            {
                table.AddRow( "  Rule Name", $"[dim]{Markup.Escape( regexAssessment.RuleName )}[/]" );
            }

            // Combined Assessment section
            table.AddRow( "", "" ); // Empty row for spacing
            table.AddRow( "[bold green]Combined (Final)[/]", "" );
            table.AddRow( "  Risk Level", GetRiskMarkup( combinedAssessment.Level ) );
            table.AddRow( "  Recommendation", GetRecommendationMarkup( combinedAssessment.Recommendation ) );

            AnsiConsole.Write( table );
            AnsiConsole.WriteLine();

            // Default to combined recommendation
            var defaultApprove = combinedAssessment.Recommendation == Recommendation.Approve;
            var approved = AnsiConsole.Confirm( "Approve this command?", defaultValue: defaultApprove );

            return Task.FromResult( approved );
        }
        finally
        {
            // Stop blinking and restore normal title
            blinkCts.Cancel();

            try
            {
                blinkTask.Wait( TimeSpan.FromMilliseconds( 500 ) );
            }
            catch
            {
                // Ignore cancellation exceptions
            }

            Console.Title = _normalTitle;
        }
    }

    private static async Task BlinkTitleAsync( CancellationToken cancellationToken )
    {
        var isAlert = true;

        try
        {
            while ( !cancellationToken.IsCancellationRequested )
            {
                Console.Title = isAlert ? _alertTitle : _normalTitle;
                isAlert = !isAlert;

                await Task.Delay( 500, cancellationToken );
            }
        }
        catch ( OperationCanceledException )
        {
            // Expected when cancellation is requested
        }
    }

    private static string GetRiskMarkup( RiskLevel level )
    {
        return level switch
        {
            RiskLevel.Low => "[green]LOW[/]",
            RiskLevel.Medium => "[yellow]MEDIUM[/]",
            RiskLevel.High => "[red]HIGH[/]",
            RiskLevel.Critical => "[red bold]CRITICAL[/]",
            _ => level.ToString()
        };
    }

    private static string GetRecommendationMarkup( Recommendation recommendation )
    {
        return recommendation switch
        {
            Recommendation.Approve => "[green]APPROVE[/]",
            Recommendation.Reject => "[red]REJECT[/]",
            _ => recommendation.ToString()
        };
    }
}
