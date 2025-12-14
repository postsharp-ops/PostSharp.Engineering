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
        RiskAssessment assessment )
#pragma warning restore CA1822
    {
        // Beep to alert the user
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
            table.AddRow( "[bold]Purpose[/]", $"[dim]{Markup.Escape( claimedPurpose )}[/]" );
            table.AddRow( "[bold]Risk Level[/]", GetRiskMarkup( assessment.Level ) );
            table.AddRow( "[bold]AI Recommendation[/]", GetRecommendationMarkup( assessment.Recommendation ) );
            table.AddRow( "[bold]Reason[/]", $"[dim]{Markup.Escape( assessment.Reason )}[/]" );

            AnsiConsole.Write( table );
            AnsiConsole.WriteLine();

            // Default to false (reject) for safety
            var approved = AnsiConsole.Confirm( "Approve this command?", defaultValue: false );

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
