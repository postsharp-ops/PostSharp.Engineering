// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

/// <summary>
/// An installation access token, with the moment GitHub says it stops working.
/// </summary>
/// <remarks>
/// GitHub issues these for one hour and there is no way to extend one, so anything that holds a token for longer than
/// it takes to use it has to be prepared to mint another. That is why the expiry travels with the token rather than
/// being discarded.
/// </remarks>
/// <param name="Token">The token, to be sent as a bearer token or handed to <c>gh</c> as <c>GH_TOKEN</c>.</param>
/// <param name="ExpiresOn">When GitHub stops accepting it.</param>
[PublicAPI]
public sealed record GitHubAppInstallationToken( string Token, DateTimeOffset ExpiresOn );
