// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Mcp.Models;

/// <summary>
/// Defines a regex-based rule for evaluating command risk.
/// </summary>
public sealed class CommandRule
{
    /// <summary>
    /// Gets the unique name of the rule.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the regex pattern to match against the command.
    /// </summary>
    public required Regex Pattern { get; init; }

    /// <summary>
    /// Gets the risk level if this rule matches.
    /// </summary>
    public required RiskLevel RiskLevel { get; init; }

    /// <summary>
    /// Gets the recommendation if this rule matches.
    /// </summary>
    public required Recommendation Recommendation { get; init; }

    /// <summary>
    /// Gets the reason explaining why this rule triggered.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Gets an optional condition function that must be satisfied for the rule to apply.
    /// If null, the rule applies whenever the pattern matches.
    /// </summary>
    public Func<CommandContext, bool>? Condition { get; init; }
}