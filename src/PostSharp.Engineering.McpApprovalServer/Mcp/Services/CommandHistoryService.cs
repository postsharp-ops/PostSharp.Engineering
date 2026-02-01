// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.McpApprovalServer.Mcp.Models;
using PostSharp.Engineering.McpApprovalServer.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PostSharp.Engineering.McpApprovalServer.Mcp.Services;

/// <summary>
/// Maintains command history for the MCP approval service.
/// Persists the last 10 commands to disk for AI context across restarts.
/// Also maintains a daily audit trail log.
/// </summary>
public sealed class CommandHistoryService
{
    private const int _maxHistorySize = 10;
    private static readonly string _historyFilePath;
    private static readonly string _historyDirectory;
    private static readonly string _auditDirectory;

    private readonly List<CommandRecord> _history = new();
    private readonly object _lock = new();

    /// <summary>
    /// Raised when the history is updated (new record added).
    /// </summary>
    public event EventHandler? HistoryUpdated;

    static CommandHistoryService()
    {
        var localAppData = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );
        _historyDirectory = Path.Combine( localAppData, "PostSharp", "McpApprovalServer" );
        _historyFilePath = Path.Combine( _historyDirectory, "recent-history.json" );
        _auditDirectory = Path.Combine( _historyDirectory, "audit" );
    }

    public CommandHistoryService()
    {
        this.LoadHistory();
    }

    /// <summary>
    /// Gets the recent command history (last 10 commands).
    /// </summary>
    public IReadOnlyList<CommandRecord> GetHistory()
    {
        lock ( this._lock )
        {
            return this._history.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Gets the recent command history for a specific session.
    /// For compatibility - returns global history.
    /// </summary>
    public IReadOnlyList<CommandRecord> GetHistory( string sessionId )
    {
        return this.GetHistory();
    }

    /// <summary>
    /// Records a command and its result. Persists to disk.
    /// </summary>
    public void Record(
        string sessionId,
        string command,
        string workingDirectory,
        string? branch,
        string claimedPurpose,
        bool approved,
        CommandResult result )
    {
        var record = new CommandRecord
        {
            Timestamp = DateTime.UtcNow,
            Command = command,
            WorkingDirectory = workingDirectory,
            ClaimedPurpose = claimedPurpose,
            Approved = approved,
            ExitCode = result.ExitCode,
            GitBranch = branch,
            Output = TruncateOutput( result.Output )
        };

        lock ( this._lock )
        {
            this._history.Add( record );

            // Keep only the last N records
            while ( this._history.Count > _maxHistorySize )
            {
                this._history.RemoveAt( 0 );
            }

            this.SaveHistory();
        }

        // Append to daily audit trail (outside lock - separate concern)
        AppendToAuditTrail( record );

        // Notify listeners
        this.HistoryUpdated?.Invoke( this, EventArgs.Empty );
    }

    /// <summary>
    /// Checks if a command was previously approved in recent history.
    /// </summary>
    public bool WasPreviouslyApproved( string sessionId, string command, string workingDirectory )
    {
        lock ( this._lock )
        {
            return this._history.Any( r =>
                                          r.Approved &&
                                          r.Command.Equals( command, StringComparison.Ordinal ) &&
                                          r.WorkingDirectory.Equals( workingDirectory, StringComparison.OrdinalIgnoreCase ) );
        }
    }

    private void LoadHistory()
    {
        try
        {
            if ( !File.Exists( _historyFilePath ) )
            {
                return;
            }

            var json = File.ReadAllText( _historyFilePath );
            var records = JsonSerializer.Deserialize<List<CommandRecord>>( json, GetJsonOptions() );

            if ( records != null )
            {
                lock ( this._lock )
                {
                    this._history.Clear();
                    this._history.AddRange( records.TakeLast( _maxHistorySize ) );
                }
            }
        }
        catch ( Exception ex )
        {
            // Log but don't fail - history is not critical
            TraceLogger.Logger.Error( "Failed to load command history", ex );
        }
    }

    private void SaveHistory()
    {
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory( _historyDirectory );

            var json = JsonSerializer.Serialize( this._history, GetJsonOptions() );
            File.WriteAllText( _historyFilePath, json );
        }
        catch ( Exception ex )
        {
            // Log but don't fail - history is not critical
            TraceLogger.Logger.Error( "Failed to save command history", ex );
        }
    }

    private static JsonSerializerOptions GetJsonOptions() => new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string? TruncateOutput( string? output )
    {
        // Truncate output to avoid huge history files
        const int maxOutputLength = 500;

        if ( output == null || output.Length <= maxOutputLength )
        {
            return output;
        }

        return output[..maxOutputLength] + "... (truncated)";
    }

    private static void AppendToAuditTrail( CommandRecord record )
    {
        try
        {
            // Ensure audit directory exists
            Directory.CreateDirectory( _auditDirectory );

            // Daily log file: audit-YYYY-MM-DD.log
            var dateStr = DateTime.UtcNow.ToString( "yyyy-MM-dd", CultureInfo.InvariantCulture );
            var auditFilePath = Path.Combine( _auditDirectory, $"audit-{dateStr}.log" );

            var gitBranch = record.GitBranch;

            // Format: timestamp | approved/rejected | command | purpose | working_dir | branch | exit_code
            var status = record.Approved ? "APPROVED" : "REJECTED";
            var timestamp = record.Timestamp.ToString( "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture );
            var exitCode = record.ExitCode?.ToString( CultureInfo.InvariantCulture ) ?? "N/A";

            // Escape pipe characters in fields
            var command = record.Command.Replace( "|", "\\|", StringComparison.Ordinal );
            var purpose = record.ClaimedPurpose.Replace( "|", "\\|", StringComparison.Ordinal );
            var workingDir = record.WorkingDirectory.Replace( "|", "\\|", StringComparison.Ordinal );

            var logLine = $"{timestamp} | {status} | {command} | {purpose} | {workingDir} | {gitBranch} | {exitCode}";

            File.AppendAllText( auditFilePath, logLine + Environment.NewLine );
        }
        catch ( Exception ex )
        {
            // Log but don't fail - audit is not critical for operation
            TraceLogger.Logger.Error( "Failed to append to audit trail", ex );
        }
    }
}