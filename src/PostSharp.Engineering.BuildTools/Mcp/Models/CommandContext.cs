// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Mcp.Models;

/// <summary>
/// Contextual information about a command execution environment, used for risk assessment.
/// </summary>
public sealed record CommandContext
{
    /// <summary>
    /// Gets the command to be executed.
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// Gets the working directory for command execution.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Gets the current git branch, if available.
    /// </summary>
    public string? CurrentBranch { get; init; }

    /// <summary>
    /// Gets the remote URL for the git repository, if available.
    /// </summary>
    public string? RemoteUrl { get; init; }

    /// <summary>
    /// Gets a value indicating whether a git merge is currently in progress.
    /// </summary>
    public bool IsMergeInProgress { get; init; }
}
