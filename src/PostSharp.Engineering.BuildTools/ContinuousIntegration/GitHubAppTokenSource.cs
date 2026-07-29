// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

/// <summary>
/// Mints GitHub App installation access tokens from the application's own private key.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is installed or uninstalled here.</b> An installation is a permanent record created once by an
/// administrator of the GitHub account, granting the application a set of permissions on a set of repositories. What
/// this class produces is a credential against an installation that already exists, and producing one changes nothing
/// on GitHub's side. It is the same exchange TeamCity performs for its build-scoped tokens, and the same one that
/// yields the built-in token of a GitHub Actions workflow.
/// </para>
/// <para>
/// <b>It does not replace <see cref="GitHubAppTokenOverride"/></b>, which stays the right mechanism for a build acting
/// on the repositories of its own organization and is simpler because the build server holds the key. The one case it
/// cannot serve is the one this exists for: a token belongs to one installation, an installation belongs to one
/// account, and a build configuration receives exactly one build-scoped token, so a job that writes to repositories in
/// two GitHub organizations cannot be served by any single one of them.
/// </para>
/// <para>
/// <b>Mint at the point of use.</b> A token lasts one hour and cannot be renewed, so a token obtained at the start of a
/// long job is not necessarily alive when the job needs it, and a dead one fails in a way that reads exactly like a
/// missing permission. Asking for one immediately before each use removes that class of failure, and the cache below
/// makes doing so cost nothing.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class GitHubAppTokenSource : IDisposable
{
    /// <summary>
    /// The public GitHub API. A GitHub Enterprise Server instance answers at <c>https://HOSTNAME/api/v3/</c>; pass an
    /// <see cref="HttpClient"/> with that base address to reach one.
    /// </summary>
    public static readonly Uri ApiBaseAddress = new( "https://api.github.com/" );

    /// <summary>
    /// How much of a token's life must remain for it to be handed out again. GitHub issues tokens for an hour, so this
    /// is what makes a cached token good for its first fifty-five minutes and no longer. The margin is not caution
    /// about the clock: a caller that receives a token is about to spend time using it, and one that expires in between
    /// produces a 401 that is indistinguishable from a permission problem.
    /// </summary>
    public static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes( 5 );

    /// <summary>
    /// GitHub refuses an assertion whose lifetime exceeds ten minutes, and refuses one whose <c>iat</c> lies in its
    /// own future, which an agent with a fast clock would produce. Eight minutes of life starting a minute ago
    /// respects both bounds with room to spare.
    /// </summary>
    private static readonly TimeSpan _assertionLifetime = TimeSpan.FromMinutes( 8 );

    private static readonly TimeSpan _assertionBackdating = TimeSpan.FromMinutes( 1 );

    private readonly GitHubAppCredentials _credentials;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Func<DateTimeOffset> _getTime;
    private readonly Dictionary<string, GitHubAppInstallationToken> _cache = new( StringComparer.Ordinal );

    // Held across the whole get-or-mint, so that two callers asking for the same repositories at once produce one
    // token rather than two. Minting twice would be harmless, but a source used from a tool that files issues in
    // parallel should not multiply requests against a rate limit for no reason.
    private readonly SemaphoreSlim _semaphore = new( 1, 1 );

    /// <param name="credentials">The application identity. <see cref="GitHubAppCredentials.TryGetFromEnvironment"/>
    /// reads it from the environment.</param>
    /// <param name="httpClient">Only tests and callers that pool connections pass this. The client is used as given,
    /// including its base address, so a test can point it at a stub.</param>
    /// <param name="getTime">Only tests pass this.</param>
    public GitHubAppTokenSource( GitHubAppCredentials credentials, HttpClient? httpClient = null, Func<DateTimeOffset>? getTime = null )
    {
        this._credentials = credentials;
        this._ownsHttpClient = httpClient == null;
        this._httpClient = httpClient ?? new HttpClient { BaseAddress = ApiBaseAddress };
        this._getTime = getTime ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Mints a token that reaches a single repository.
    /// </summary>
    public Task<GitHubAppInstallationToken> GetInstallationTokenAsync( GitHubRepository repository, CancellationToken cancellationToken = default )
        => this.GetInstallationTokenAsync( repository.Owner, [repository.Name], cancellationToken );

    /// <summary>
    /// Mints a token that reaches a single repository.
    /// </summary>
    public Task<GitHubAppInstallationToken> GetInstallationTokenAsync( string owner, string repository, CancellationToken cancellationToken = default )
        => this.GetInstallationTokenAsync( owner, [repository], cancellationToken );

    /// <summary>
    /// Mints a token that reaches the given repositories of one GitHub account.
    /// </summary>
    /// <remarks>
    /// The token is narrowed to exactly these repositories even when the installation covers more, so a token handed to
    /// a job that writes to one repository cannot be spent on another. The installation itself is found from the first
    /// repository, which works for an organization and for a user account alike, unlike the per-account endpoints.
    /// </remarks>
    /// <param name="owner">The GitHub account owning the repositories.</param>
    /// <param name="repositories">Repository names, without the owner. Must not be empty.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<GitHubAppInstallationToken> GetInstallationTokenAsync(
        string owner,
        ImmutableArray<string> repositories,
        CancellationToken cancellationToken = default )
    {
        if ( repositories.IsDefaultOrEmpty )
        {
            throw new ArgumentException( "At least one repository is required.", nameof(repositories) );
        }

        // Sorted, because the list is also the cache key: the same pair of repositories given in the other order has to
        // hit the same entry.
        var names = repositories.OrderBy( r => r, StringComparer.OrdinalIgnoreCase ).ToImmutableArray();
        var cacheKey = $"{owner}/{string.Join( ",", names )}";

        await this._semaphore.WaitAsync( cancellationToken );

        try
        {
            if ( this._cache.TryGetValue( cacheKey, out var cached ) && cached.ExpiresOn - this._getTime() > RenewalMargin )
            {
                return cached;
            }

            var assertion = this.CreateAssertion( this._getTime() );
            var installationId = await this.GetInstallationIdAsync( assertion, owner, names[0], cancellationToken );
            var token = await this.CreateInstallationTokenAsync( assertion, installationId, names, cancellationToken );

            this._cache[cacheKey] = token;

            return token;
        }
        finally
        {
            this._semaphore.Release();
        }
    }

    /// <summary>
    /// Signs the JSON Web Token that authenticates as the application itself. Internal so that a test can assert its
    /// shape without reaching GitHub.
    /// </summary>
    internal string CreateAssertion( DateTimeOffset now )
    {
        var issuedAt = now.Subtract( _assertionBackdating ).ToUnixTimeSeconds();
        var expiresAt = now.Add( _assertionLifetime ).ToUnixTimeSeconds();

        var header = Base64UrlEncode( Encoding.UTF8.GetBytes( """{"alg":"RS256","typ":"JWT"}""" ) );

        var payload = Base64UrlEncode(
            Encoding.UTF8.GetBytes(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $$"""{"iat":{{issuedAt}},"exp":{{expiresAt}},"iss":"{{this._credentials.AppId}}"}""" ) ) );

        var signingInput = $"{header}.{payload}";

        using var rsa = RSA.Create();

        try
        {
            rsa.ImportFromPem( this._credentials.PrivateKeyPem );
        }
        catch ( ArgumentException e )
        {
            // The key must not be quoted, and the underlying message quotes nothing useful, so the shape of what
            // arrived is described instead: that is what distinguishes a truncated secret from an unset one.
            throw new InvalidOperationException(
                $"The GitHub App private key could not be read ({e.Message}). It has {this._credentials.PrivateKeyPem.Length} "
                + $"characters and {(this._credentials.PrivateKeyPem.StartsWith( "-----BEGIN", StringComparison.Ordinal ) ? "starts" : "does not start")} "
                + $"with a PEM header. Set '{EnvironmentVariableNames.GitHubAppPrivateKey}' to the PEM file, or to its base64 encoding.",
                e );
        }

        var signature = rsa.SignData( Encoding.UTF8.GetBytes( signingInput ), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1 );

        return $"{signingInput}.{Base64UrlEncode( signature )}";
    }

    private async Task<long> GetInstallationIdAsync( string assertion, string owner, string repository, CancellationToken cancellationToken )
    {
        using var request = this.CreateRequest( HttpMethod.Get, $"repos/{owner}/{repository}/installation", assertion );

        using var response = await this._httpClient.SendAsync( request, cancellationToken );

        var body = await response.Content.ReadAsStringAsync( cancellationToken );

        if ( !response.IsSuccessStatusCode )
        {
            throw new InvalidOperationException(
                $"The GitHub App is not installed on '{owner}/{repository}', or cannot see it: "
                + $"GET {request.RequestUri} returned {(int) response.StatusCode} {response.ReasonPhrase}. {body}" );
        }

        using var document = JsonDocument.Parse( body );

        return document.RootElement.GetProperty( "id" ).GetInt64();
    }

    private async Task<GitHubAppInstallationToken> CreateInstallationTokenAsync(
        string assertion,
        long installationId,
        ImmutableArray<string> repositories,
        CancellationToken cancellationToken )
    {
        using var request = this.CreateRequest( HttpMethod.Post, $"app/installations/{installationId}/access_tokens", assertion );

        var payload = new StringBuilder( """{"repositories":[""" );

        for ( var i = 0; i < repositories.Length; i++ )
        {
            if ( i > 0 )
            {
                payload.Append( ',' );
            }

            payload.Append( JsonSerializer.Serialize( repositories[i] ) );
        }

        payload.Append( "]}" );

        request.Content = new StringContent( payload.ToString(), Encoding.UTF8, "application/json" );

        using var response = await this._httpClient.SendAsync( request, cancellationToken );

        var body = await response.Content.ReadAsStringAsync( cancellationToken );

        if ( !response.IsSuccessStatusCode )
        {
            throw new InvalidOperationException(
                $"The GitHub App could not obtain an access token for installation {installationId}: "
                + $"POST {request.RequestUri} returned {(int) response.StatusCode} {response.ReasonPhrase}. {body}" );
        }

        using var document = JsonDocument.Parse( body );

        var token = document.RootElement.GetProperty( "token" ).GetString()
                    ?? throw new InvalidOperationException( "GitHub returned an access token response without a token." );

        var expiresOn = document.RootElement.TryGetProperty( "expires_at", out var expiresAt )
                        && DateTimeOffset.TryParse(
                            expiresAt.GetString(),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                            out var parsed )
            ? parsed

            // GitHub always sends it. A response that somehow does not is treated as already expired, so a missing
            // expiry can only ever cause an extra mint, never a request carrying a dead token.
            : this._getTime();

        return new GitHubAppInstallationToken( token, expiresOn );
    }

    private HttpRequestMessage CreateRequest( HttpMethod method, string relativeUrl, string assertion )
    {
        // Relative to the client's base address when it has one, so that a test can host the stub anywhere and a
        // GitHub Enterprise caller can pass its own client.
        var uri = this._httpClient.BaseAddress == null ? new Uri( ApiBaseAddress, relativeUrl ) : new Uri( this._httpClient.BaseAddress, relativeUrl );

        var request = new HttpRequestMessage( method, uri );
        request.Headers.Authorization = new AuthenticationHeaderValue( "Bearer", assertion );
        request.Headers.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/vnd.github+json" ) );
        request.Headers.Add( "X-GitHub-Api-Version", "2022-11-28" );

        // GitHub rejects a request without one.
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue( "PostSharp.Engineering", typeof(GitHubAppTokenSource).Assembly.GetName().Version?.ToString() ?? "1.0" ) );

        return request;
    }

    private static string Base64UrlEncode( byte[] bytes )
        => Convert.ToBase64String( bytes ).TrimEnd( '=' ).Replace( '+', '-' ).Replace( '/', '_' );

    public void Dispose()
    {
        this._semaphore.Dispose();

        if ( this._ownsHttpClient )
        {
            this._httpClient.Dispose();
        }
    }
}
