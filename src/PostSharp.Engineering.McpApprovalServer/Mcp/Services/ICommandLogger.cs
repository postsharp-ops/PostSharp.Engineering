// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.McpApprovalServer.Mcp.Services;

/// <summary>
/// Interface for logging command execution events.
/// Allows different implementations for console-based and GUI-based logging.
/// </summary>
public interface ICommandLogger
{
    /// <summary>
    /// Logs an informational message.
    /// </summary>
    void LogInfo( string message );

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    void LogWarning( string message );

    /// <summary>
    /// Logs an error message.
    /// </summary>
    void LogError( string message );

    /// <summary>
    /// Logs a success message.
    /// </summary>
    void LogSuccess( string message );

    /// <summary>
    /// Logs a section header/rule.
    /// </summary>
    void LogSection( string title );
}
