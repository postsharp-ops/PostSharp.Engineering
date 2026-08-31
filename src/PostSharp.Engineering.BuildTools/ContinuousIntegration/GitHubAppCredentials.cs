// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

/// <summary>
/// The identity of a GitHub App: the numeric application identifier and the RSA private key it signs its assertions
/// with. Together these mint installation access tokens through <see cref="GitHubAppTokenSource"/>.
/// </summary>
/// <remarks>
/// This is an addition to <see cref="GitHubAppTokenOverride"/>, not a replacement for it, and both remain supported.
/// TeamCity performs exactly this exchange from the private key held by its GitHub App connection; the only thing it
/// cannot do is span two GitHub accounts, because it hands a build configuration a single token and a token belongs to
/// a single installation.
/// </remarks>
[PublicAPI]
public sealed record GitHubAppCredentials( string AppId, string PrivateKeyPem )
{
    /// <summary>
    /// Reads the credentials from <c>GITHUB_APP_ID</c> and <c>GITHUB_APP_PRIVATE_KEY</c>. Inside a build container
    /// these arrive through the generated <c>DockerBuild.ps1</c>, which forwards every name listed in
    /// <see cref="EnvironmentVariableNames.All"/>, under a <c>CLAUDE_</c> prefix when the container runs an agent
    /// session.
    /// </summary>
    /// <param name="credentials">The credentials, when both variables are set.</param>
    /// <param name="error">Why not, otherwise. Names both variables rather than only the one looked up first, because
    /// a process that has neither is running where they were never configured.</param>
    public static bool TryGetFromEnvironment( [NotNullWhen( true )] out GitHubAppCredentials? credentials, [NotNullWhen( false )] out string? error )
    {
        var appId = Environment.GetEnvironmentVariable( EnvironmentVariableNames.GitHubAppId );
        var privateKey = Environment.GetEnvironmentVariable( EnvironmentVariableNames.GitHubAppPrivateKey );

        if ( string.IsNullOrWhiteSpace( appId ) || string.IsNullOrWhiteSpace( privateKey ) )
        {
            error = $"A GitHub App identity is required. Set both the '{EnvironmentVariableNames.GitHubAppId}' and "
                    + $"'{EnvironmentVariableNames.GitHubAppPrivateKey}' environment variables.";

            credentials = null;

            return false;
        }

        credentials = new GitHubAppCredentials( appId.Trim(), NormalizePrivateKey( privateKey ) );
        error = null;

        return true;
    }

    /// <summary>
    /// Accepts the private key in any of the three shapes it survives the journey from a secret store to a process
    /// environment in: the PEM text itself, that text base64-encoded, or the text with its line breaks written as the
    /// two characters <c>\n</c>.
    /// </summary>
    /// <remarks>
    /// Base64 is the shape to prefer, and the reason is the container. <c>DockerBuild.ps1</c> writes the forwarded
    /// variables into a generated <c>Init.g.ps1</c> as single-quoted PowerShell literals; a multi-line value is legal
    /// there but survives only as long as nothing along the way (a build parameter, a shell, a Docker argument)
    /// rewrites its line endings. A single-line value has nothing to rewrite.
    /// </remarks>
    public static string NormalizePrivateKey( string value )
    {
        var key = value.Trim();

        if ( !key.Contains( "-----BEGIN", StringComparison.Ordinal ) )
        {
            try
            {
                key = Encoding.UTF8.GetString( Convert.FromBase64String( key ) ).Trim();
            }
            catch ( FormatException )
            {
                // Not base64 either. Left as it is, so that the failure comes from the key parser with a message
                // about the key rather than from here with a message about base64.
            }
        }

        // Only when there is no real line break, so that a well-formed key containing a literal backslash-n
        // sequence (which a PEM body cannot) is never rewritten.
        if ( !key.Contains( '\n', StringComparison.Ordinal ) )
        {
            key = key.Replace( "\\n", "\n", StringComparison.Ordinal );
        }

        return key;
    }
}
