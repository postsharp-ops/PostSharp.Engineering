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
internal record GitHubAppBuildScopedTokenSettings( string ConnectionId, ImmutableArray<string> TargetRepositories );
