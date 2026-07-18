// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.Collections.Immutable;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;

/// <summary>
/// Settings of the TeamCity <c>gitHubAppBuildScopedToken</c> build feature, which issues a GitHub App installation
/// token when the build starts and revokes it when the build completes.
/// </summary>
/// <param name="ConnectionId">Identifier of the TeamCity GitHub App connection. See <see cref="GitHubAppConnections"/>.
/// The connection must have <c>Enable build-scoped tokens</c> set.</param>
/// <param name="TargetRepositories">Names of the repositories the token gives access to, without the organization.
/// Tokens are fine-grained: they only reach the repositories listed here, and TeamCity has no wildcard standing for
/// all repositories, so a build that pushes to its source dependencies must list them next to its own repository.</param>
/// <param name="ParameterName">Name of the TeamCity parameter that receives the token, including the <c>env.</c> prefix
/// that turns it into an environment variable. Defaults to <see cref="DefaultParameterName"/>. A build configuration
/// that hands the token over to a process reading another variable overrides this.</param>
internal record GitHubAppBuildScopedTokenSettings(
    string ConnectionId,
    ImmutableArray<string> TargetRepositories,
    string ParameterName = GitHubAppBuildScopedTokenSettings.DefaultParameterName )
{
    /// <summary>
    /// The parameter that the build steps and the build tools read by default.
    /// </summary>
    public const string DefaultParameterName = "env." + EnvironmentVariableNames.GitHubToken;
}
