// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

/// <summary>
/// Identifiers of the TeamCity GitHub App connections. A connection is bound to a GitHub organization, and it can only
/// issue tokens for the repositories of that organization.
/// </summary>
[PublicAPI]
public static class GitHubAppConnections
{
    /// <summary>
    /// Connection to the app named <c>TeamCity - Metalama org</c>, which serves the <c>metalama</c> organization.
    /// </summary>
    public const string Metalama = "PROJECT_EXT_58";

    /// <summary>
    /// Connection to the app named <c>TeamCity (postsharp org)</c>, which serves the <c>postsharp</c> organization.
    /// </summary>
    public const string PostSharp = "PROJECT_EXT_59";

    /// <summary>
    /// Connection to the app that serves the <c>postsharp-ops</c> organization, formerly named <c>sharpcrafters-sro</c>.
    /// </summary>
    public const string PostSharpOps = "PROJECT_EXT_13";
}
