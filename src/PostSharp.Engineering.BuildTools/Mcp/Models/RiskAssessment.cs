// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools.Mcp.Models;

/// <summary>
/// Represents the risk level of a command.
/// </summary>
public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Represents the recommendation for a command.
/// </summary>
public enum Recommendation
{
    None,
    Approve,
    Reject
}

/// <summary>
/// Represents the result of a risk analysis for a command.
/// </summary>
public sealed class RiskAssessment
{
    public required RiskLevel Level { get; init; }

    public required Recommendation Recommendation { get; init; }

    public required string Reason { get; init; }

    /// <summary>
    /// Gets the name of the rule that triggered this assessment (for regex-based rules).
    /// Null for AI-driven assessments.
    /// </summary>
    public string? RuleName { get; init; }

    public static RiskAssessment Default( string reason )
    {
        return new RiskAssessment
        {
            Level = RiskLevel.Medium,
            Recommendation = Recommendation.Approve,
            Reason = reason
        };
    }

    public static RiskAssessment Parse( string output )
    {
        var level = RiskLevel.Medium;
        var recommendation = Recommendation.Approve;
        var reason = "Unable to parse risk assessment";

        var lines = output.Split( '\n', StringSplitOptions.RemoveEmptyEntries );

        foreach ( var line in lines )
        {
            var trimmedLine = line.Trim();

            if ( trimmedLine.StartsWith( "RISK:", StringComparison.OrdinalIgnoreCase ) )
            {
                var value = trimmedLine.Substring( 5 ).Trim();

                level = value.ToUpperInvariant() switch
                {
                    "LOW" => RiskLevel.Low,
                    "MEDIUM" => RiskLevel.Medium,
                    "HIGH" => RiskLevel.High,
                    "CRITICAL" => RiskLevel.Critical,
                    _ => RiskLevel.Medium
                };
            }
            else if ( trimmedLine.StartsWith( "RECOMMEND:", StringComparison.OrdinalIgnoreCase ) )
            {
                var value = trimmedLine.Substring( 10 ).Trim();

                recommendation = value.ToUpperInvariant() switch
                {
                    "APPROVE" => Recommendation.Approve,
                    "REJECT" => Recommendation.Reject,
                    _ => Recommendation.Approve
                };
            }
            else if ( trimmedLine.StartsWith( "REASON:", StringComparison.OrdinalIgnoreCase ) )
            {
                reason = trimmedLine.Substring( 7 ).Trim();
            }
        }

        return new RiskAssessment
        {
            Level = level,
            Recommendation = recommendation,
            Reason = reason
        };
    }
}
