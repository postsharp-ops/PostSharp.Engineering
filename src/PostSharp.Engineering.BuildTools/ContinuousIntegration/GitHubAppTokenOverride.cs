// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

/// <summary>
/// Replaces, for a single build configuration, the GitHub App connection and the parameter that the build-scoped token
/// would otherwise inherit from the repository. A build configuration issues exactly one token, so this substitutes for
/// the repository-wide connection rather than adding a second token.
/// </summary>
/// <remarks>
/// This exists because a build configuration can act under an identity other than the build system's. The autonomous
/// agent is the case in point: it opens pull requests as itself, through an app that intentionally holds fewer
/// permissions than the app the rest of the builds use. See <see cref="GitHubAppConnections.MetalamaAgent"/>.
/// </remarks>
/// <param name="ConnectionId">Identifier of the TeamCity GitHub App connection that issues the token. See
/// <see cref="GitHubAppConnections"/>. The connection must serve the same GitHub organization as the repository,
/// because a token cannot reach the repositories of another organization.</param>
/// <param name="ParameterName">Name of the TeamCity parameter that receives the token, including the <c>env.</c>
/// prefix. When <c>null</c>, the token lands in the usual <c>env.GITHUB_TOKEN</c>. A build configuration that hands the
/// token to a process reading another variable sets this; in particular, <c>DockerBuild.ps1</c> forwards a host
/// variable into the container only when it carries a <c>CLAUDE_</c> prefix, so a token meant for the containerized
/// agent must be written to <c>env.CLAUDE_GITHUB_TOKEN</c> to arrive as <c>GITHUB_TOKEN</c> inside the container.</param>
[PublicAPI]
public sealed record GitHubAppTokenOverride( string ConnectionId, string? ParameterName = null )
{
    /// <summary>
    /// Gets the parameter name to emit, falling back to the default when none is set.
    /// </summary>
    internal string EffectiveParameterName => this.ParameterName ?? GitHubAppBuildScopedTokenSettings.DefaultParameterName;
}
