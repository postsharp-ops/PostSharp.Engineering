// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Extensions.Logging;
using System;

namespace PostSharp.Engineering.McpApprovalServer.Services;

/// <summary>
/// <see cref="ILoggerProvider"/> that routes all Microsoft.Extensions.Logging output
/// to <see cref="TraceLogger"/> (file-based log at
/// <c>%LOCALAPPDATA%\PostSharp\McpApprovalServer\logs\</c>).
/// </summary>
public sealed class TraceLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger( string categoryName ) => new Logger( categoryName );

    public void Dispose() { }

    private sealed class Logger : ILogger
    {
        private readonly string _category;

        public Logger( string category )
        {
            this._category = category;
        }

        public IDisposable? BeginScope<TState>( TState state ) where TState : notnull => null;

        public bool IsEnabled( LogLevel logLevel ) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter )
        {
            if ( !this.IsEnabled( logLevel ) )
            {
                return;
            }

            var message = formatter( state, exception );

            if ( exception != null )
            {
                message = $"{message} | {exception}";
            }

            switch ( logLevel )
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    TraceLogger.Logger.Trace( this._category, message );

                    break;

                case LogLevel.Information:
                    TraceLogger.Logger.Info( $"[{this._category}] {message}" );

                    break;

                case LogLevel.Warning:
                    TraceLogger.Logger.Warn( $"[{this._category}] {message}" );

                    break;

                case LogLevel.Error:
                case LogLevel.Critical:
                    TraceLogger.Logger.Error( $"[{this._category}] {message}" );

                    break;
            }
        }
    }
}
