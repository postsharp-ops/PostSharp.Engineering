// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

/// <summary>
/// References to the TeamCity GitHub App connections. A connection is bound to a GitHub organization, and it can only
/// issue tokens for the repositories of that organization.
/// </summary>
/// <remarks>
/// The values are TeamCity parameter references, not connection identifiers. TeamCity assigns a connection an opaque
/// identifier (<c>PROJECT_EXT_nn</c>) that depends on the order in which the connections were created, so it cannot be
/// hardcoded here. Instead, each parameter below must be defined on the TeamCity root project and set to the
/// identifier of the corresponding connection.
/// </remarks>
[PublicAPI]
public static class GitHubAppConnections
{
    /// <summary>
    /// Connection to the app named <c>TeamCity - Metalama org</c>, which serves the <c>metalama</c> organization.
    /// </summary>
    public const string Metalama = "%GITHUB_CONNECTION_METALAMA%";

    /// <summary>
    /// Connection to the app named <c>TeamCity (postsharp org)</c>, which serves the <c>postsharp</c> organization.
    /// </summary>
    public const string PostSharp = "%GITHUB_CONNECTION_POSTSHARP%";

    /// <summary>
    /// Connection to the app that serves the <c>postsharp-ops</c> organization, formerly named <c>sharpcrafters-sro</c>.
    /// </summary>
    public const string PostSharpOps = "%GITHUB_CONNECTION_POSTSHARP_OPS%";

    /// <summary>
    /// Connection to the app named <c>Metalama Agent</c>, which serves the <c>metalama</c> organization. This is the
    /// identity of the autonomous agent, not of the build system: it deliberately holds only the permissions the agent
    /// needs to open pull requests and comment on issues, and specifically has no policy or ruleset bypass rights,
    /// which <see cref="Metalama"/> does have. The two identities must not be merged. Because a build configuration
    /// issues a single token, this connection is selected per build configuration through
    /// <see cref="Model.AdditionalCiBuildConfiguration.GitHubAppToken"/> instead of per repository.
    /// </summary>
    public const string MetalamaAgent = "%GITHUB_CONNECTION_METALAMA_AGENT%";
}
