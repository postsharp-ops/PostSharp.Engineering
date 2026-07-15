// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build;

/// <summary>
/// Helpers to select which deployment a <c>publish</c> or <c>swap</c> command acts on. A deployment is a named group
/// of publishers (or swappers) sharing the same <c>EffectiveDeploymentName</c>. A <c>null</c> selection means the
/// single, implicit deployment (i.e. act on every publisher or swapper).
/// </summary>
internal static class DeploymentSelection
{
    /// <summary>
    /// Validates the deployment requested on the command line against the <paramref name="deploymentNames"/> defined by
    /// the configuration. Writes an error and returns <c>false</c> when no deployment was requested but the configuration
    /// defines more than one, or — when <paramref name="validateExists"/> is set — when the requested deployment is
    /// unknown. When it returns <c>true</c>, the requested name (or <c>null</c> for "all") can be used directly as the
    /// filter.
    /// </summary>
    public static bool TryValidate(
        ConsoleHelper console,
        IReadOnlyCollection<string> deploymentNames,
        string? requestedDeploymentName,
        string actionName,
        bool validateExists )
    {
        if ( requestedDeploymentName == null )
        {
            if ( deploymentNames.Count > 1 )
            {
                console.WriteError(
                    $"The --deployment option is required to {actionName} because the configuration defines multiple deployments: "
                    + $"{string.Join( ", ", deploymentNames )}." );

                return false;
            }

            return true;
        }

        if ( validateExists && !deploymentNames.Contains( requestedDeploymentName, StringComparer.Ordinal ) )
        {
            console.WriteError(
                $"Unknown deployment '{requestedDeploymentName}'. Available deployments: {string.Join( ", ", deploymentNames )}." );

            return false;
        }

        return true;
    }
}
