// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Mcp;

/// <summary>
/// Settings for the MCP server command.
/// </summary>
public sealed class McpServerCommandSettings : CommandSettings
{
    [CommandOption( "--port" )]
    [Description( "The port to listen on. Use 0 for dynamic port assignment (default)." )]
    [DefaultValue( 0 )]
    public int Port { get; init; }

    [CommandOption( "--port-file" )]
    [Description( "File path to write the assigned port number. Used for dynamic port discovery." )]
    public string? PortFile { get; init; }

    [CommandOption( "--verbose" )]
    [Description( "Enable verbose logging including HTTP requests." )]
    [DefaultValue( false )]
    public bool Verbose { get; init; }

    [CommandOption( "--secret" )]
    [Description( "Security token for authenticating requests. Required for production use." )]
    public string? Secret { get; init; }
}