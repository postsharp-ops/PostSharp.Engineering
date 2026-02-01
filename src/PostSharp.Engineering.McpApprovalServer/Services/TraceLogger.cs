// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace PostSharp.Engineering.McpApprovalServer.Services;

/// <summary>
/// Simple file-based trace logger for detailed diagnostics.
/// Log file is created at program start with timestamp-based filename.
/// </summary>
public sealed class TraceLogger : IDisposable
{
    private static readonly Lazy<TraceLogger> _instance = new( () => new TraceLogger() );
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private readonly string _logFilePath;
    private bool _disposed;

    public static TraceLogger Logger => _instance.Value;

    public string LogFilePath => this._logFilePath;

    private TraceLogger()
    {
        var localAppData = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );
        var logDirectory = Path.Combine( localAppData, "PostSharp", "McpApprovalServer", "logs" );

        Directory.CreateDirectory( logDirectory );

        var timestamp = DateTime.Now.ToString( "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture );
        this._logFilePath = Path.Combine( logDirectory, $"mcp-{timestamp}.log" );

        this._writer = new StreamWriter( this._logFilePath, append: false ) { AutoFlush = true };

        this.Info( $"Log started: {this._logFilePath}" );
        this.Info( $"Process ID: {Environment.ProcessId}" );
    }

    public void Trace( string category, string message )
    {
        this.Write( "TRACE", category, message );
    }

    public void Info( string message )
    {
        this.Write( "INFO", null, message );
    }

    public void Warn( string message )
    {
        this.Write( "WARN", null, message );
    }

    public void Error( string message )
    {
        this.Write( "ERROR", null, message );
    }

    public void Error( string message, Exception ex )
    {
        this.Write( "ERROR", null, $"{message}: {ex}" );
    }

    private void Write( string level, string? category, string message )
    {
        if ( this._disposed )
        {
            return;
        }

        var timestamp = DateTime.Now.ToString( "HH:mm:ss.fff", CultureInfo.InvariantCulture );
        var categoryPart = category != null ? $"[{category}] " : "";
        var line = $"{timestamp} [{level}] {categoryPart}{message}";

        lock ( this._lock )
        {
            try
            {
                this._writer.WriteLine( line );
                Debug.WriteLine( line );
            }
            catch
            {
                // Ignore write errors
            }
        }
    }

    public void Dispose()
    {
        if ( this._disposed )
        {
            return;
        }

        this._disposed = true;

        lock ( this._lock )
        {
            this.Info( "Log closed" );
            this._writer.Dispose();
        }
    }
}
