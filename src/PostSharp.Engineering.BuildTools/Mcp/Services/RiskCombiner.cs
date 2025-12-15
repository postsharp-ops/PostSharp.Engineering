// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Mcp.Models;
using System;

namespace PostSharp.Engineering.BuildTools.Mcp.Services;

/// <summary>
/// Combines two risk assessments into a single assessment by taking the maximum (most restrictive) risk.
/// </summary>
public static class RiskCombiner
{
    /// <summary>
    /// Combines two risk assessments by taking the maximum risk level and most restrictive recommendation.
    /// </summary>
    /// <param name="aiAssessment">The AI-driven risk assessment.</param>
    /// <param name="regexAssessment">The regex-based risk assessment.</param>
    /// <returns>A combined risk assessment with the maximum risk level and most restrictive recommendation.</returns>
    public static RiskAssessment Combine( RiskAssessment aiAssessment, RiskAssessment regexAssessment )
    {
        // Take the maximum (most restrictive) risk level
        var maxLevel = (RiskLevel) Math.Max( (int) aiAssessment.Level, (int) regexAssessment.Level );

        // If either recommends REJECT, the combined recommendation is REJECT
        var combinedRecommendation = aiAssessment.Recommendation == Recommendation.Reject ||
                                     regexAssessment.Recommendation == Recommendation.Reject
            ? Recommendation.Reject
            : Recommendation.Approve;

        // Combine reasons from both assessments
        var combinedReason = CombineReasons( aiAssessment, regexAssessment );

        return new RiskAssessment
        {
            Level = maxLevel,
            Recommendation = combinedRecommendation,
            Reason = combinedReason,
            RuleName = null // Combined assessment doesn't have a specific rule name
        };
    }

    private static string CombineReasons( RiskAssessment aiAssessment, RiskAssessment regexAssessment )
    {
        // If both have the same reason, don't duplicate
        if ( aiAssessment.Reason == regexAssessment.Reason )
        {
            return aiAssessment.Reason;
        }

        // If one is more restrictive, lead with that one
        if ( aiAssessment.Level > regexAssessment.Level ||
             (aiAssessment.Level == regexAssessment.Level && aiAssessment.Recommendation == Recommendation.Reject) )
        {
            return $"{aiAssessment.Reason}; {regexAssessment.Reason}";
        }

        return $"{regexAssessment.Reason}; {aiAssessment.Reason}";
    }
}
