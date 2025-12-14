// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Mcp.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Mcp.Services;

/// <summary>
/// Maintains per-session command history for the MCP approval service.
/// This history is maintained by the service (not provided by the client) to enable
/// detection of suspicious patterns.
/// </summary>
public sealed class CommandHistoryService
{
    private readonly ConcurrentDictionary<string, List<CommandRecord>> _sessions = new();
    private readonly object _lock = new();

    public IReadOnlyList<CommandRecord> GetHistory( string sessionId )
    {
        if ( this._sessions.TryGetValue( sessionId, out var history ) )
        {
            lock ( this._lock )
            {
                return history.ToList().AsReadOnly();
            }
        }

        return Array.Empty<CommandRecord>();
    }

    public void Record(
        string sessionId,
        string command,
        string claimedPurpose,
        bool approved,
        CommandResult result )
    {
        var record = new CommandRecord
        {
            Timestamp = DateTime.UtcNow,
            Command = command,
            ClaimedPurpose = claimedPurpose,
            Approved = approved,
            ExitCode = result.ExitCode,
            Output = result.Output
        };

        this._sessions.AddOrUpdate(
            sessionId,
            _ => new List<CommandRecord> { record },
            ( _, list ) =>
            {
                lock ( this._lock )
                {
                    list.Add( record );
                }

                return list;
            } );
    }

    public void ClearSession( string sessionId )
    {
        this._sessions.TryRemove( sessionId, out _ );
    }
}
