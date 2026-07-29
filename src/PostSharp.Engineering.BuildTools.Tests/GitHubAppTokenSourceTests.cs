// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Tools.GitHub;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// A build-scoped token cannot reach the repositories of another GitHub organization, because GitHub issues a token per
/// installation and an installation belongs to one account. Minting from the app key is how a job that writes to two
/// organizations gets one token for each; these tests pin the exchange that does it, and the shapes the private key
/// arrives in after passing through a secret store, a build parameter and a container.
/// </summary>
public sealed class GitHubAppTokenSourceTests
{
    private static readonly DateTimeOffset _now = new( 2026, 7, 29, 5, 0, 0, TimeSpan.Zero );

    /// <summary>
    /// A throw-away key, generated here rather than checked in: a private key in a repository is a finding whatever it
    /// unlocks, and nothing here needs the same key twice.
    /// </summary>
    private static string CreatePrivateKeyPem()
    {
        using var rsa = RSA.Create( 2048 );

        return rsa.ExportRSAPrivateKeyPem();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> _respond;

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> RequestBodies { get; } = [];

        public StubHandler( Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> respond )
        {
            this._respond = respond;
        }

        protected override async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
        {
            this.Requests.Add( request );

            this.RequestBodies.Add( request.Content == null ? "" : await request.Content.ReadAsStringAsync( cancellationToken ) );

            var (status, body) = this._respond( request );

            return new HttpResponseMessage( status ) { Content = new StringContent( body, Encoding.UTF8, "application/json" ) };
        }
    }

    private static (GitHubAppTokenSource Source, StubHandler Handler, HttpClient Client) CreateSource(
        Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> respond )
    {
        var handler = new StubHandler( respond );
        var client = new HttpClient( handler ) { BaseAddress = GitHubAppTokenSource.ApiBaseAddress };
        var source = new GitHubAppTokenSource( new GitHubAppCredentials( "12345", CreatePrivateKeyPem() ), client, () => _now );

        return (source, handler, client);
    }

    private static (HttpStatusCode, string) RespondHappily( HttpRequestMessage request, string expiresAt = "2026-07-29T06:00:00Z" )
        => request.RequestUri!.AbsolutePath.EndsWith( "/installation", StringComparison.Ordinal )
            ? (HttpStatusCode.OK, """{"id":98765}""")
            : (HttpStatusCode.Created, $$"""{"token":"ghs_the_token","expires_at":"{{expiresAt}}"}""");

    /// <summary>
    /// The whole exchange: find the installation covering the repository, then trade the app assertion for that
    /// installation's token. Nothing is installed here, and nothing is cleaned up: the installation is a permanent
    /// record an administrator made once, and only the credential against it is short-lived.
    /// </summary>
    [Fact]
    public async Task TheTokenIsObtainedFromTheInstallationCoveringTheRepository()
    {
        var (source, handler, client) = CreateSource( r => RespondHappily( r ) );

        using ( source )
        using ( client )
        {
            var token = await source.GetInstallationTokenAsync( "metalama", "Metalama" );

            Assert.Equal( "ghs_the_token", token.Token );
            Assert.Equal( new DateTimeOffset( 2026, 7, 29, 6, 0, 0, TimeSpan.Zero ), token.ExpiresOn );

            Assert.Equal( 2, handler.Requests.Count );
            Assert.Equal( "https://api.github.com/repos/metalama/Metalama/installation", handler.Requests[0].RequestUri!.ToString() );
            Assert.Equal( "https://api.github.com/app/installations/98765/access_tokens", handler.Requests[1].RequestUri!.ToString() );
        }
    }

    /// <summary>
    /// The token is narrowed to the repositories asked for even when the installation covers more, so a token minted to
    /// file an issue in one repository cannot be spent on another.
    /// </summary>
    [Fact]
    public async Task TheTokenIsScopedToTheRequestedRepositories()
    {
        var (source, handler, client) = CreateSource( r => RespondHappily( r ) );

        using ( source )
        using ( client )
        {
            _ = await source.GetInstallationTokenAsync( "metalama", ["Metalama.Vsx.Public", "Metalama"] );

            using var body = JsonDocument.Parse( handler.RequestBodies[1] );
            var requested = body.RootElement.GetProperty( "repositories" );

            Assert.Equal( 2, requested.GetArrayLength() );

            // Sorted, because the sorted list is also the cache key: the same pair given in the other order has to hit
            // the same entry rather than mint a second token.
            Assert.Equal( "Metalama", requested[0].GetString() );
            Assert.Equal( "Metalama.Vsx.Public", requested[1].GetString() );

            // The installation is found from a repository, which works for an organization and a user alike.
            Assert.Equal( "https://api.github.com/repos/metalama/Metalama/installation", handler.Requests[0].RequestUri!.ToString() );
        }
    }

    /// <summary>
    /// Two owners cannot share a token, which is the entire reason this exists beside the build-scoped one.
    /// </summary>
    [Fact]
    public async Task EachOwnerIsAnInstallationOfItsOwn()
    {
        var (source, handler, client) = CreateSource( r => RespondHappily( r ) );

        using ( source )
        using ( client )
        {
            _ = await source.GetInstallationTokenAsync( "metalama", "Metalama" );
            _ = await source.GetInstallationTokenAsync( "postsharp", "PostSharp.Public" );

            Assert.Equal( 4, handler.Requests.Count );
            Assert.Equal( "https://api.github.com/repos/postsharp/PostSharp.Public/installation", handler.Requests[2].RequestUri!.ToString() );
        }
    }

    /// <summary>
    /// A token minted less than fifty-five minutes ago is handed out again, which is what makes minting immediately
    /// before each use, rather than once at the start of a job, cost nothing.
    /// </summary>
    [Fact]
    public async Task AUsableTokenIsReused()
    {
        var (source, handler, client) = CreateSource( r => RespondHappily( r ) );

        using ( source )
        using ( client )
        {
            var first = await source.GetInstallationTokenAsync( "metalama", "Metalama" );
            var second = await source.GetInstallationTokenAsync( "metalama", "Metalama" );

            Assert.Equal( first.Token, second.Token );
            Assert.Equal( 2, handler.Requests.Count );
        }
    }

    /// <summary>
    /// The other half of the same rule, and the one that matters more: past fifty-five minutes of a sixty-minute token,
    /// a fresh one is minted. Reusing it would produce a 401, which reads exactly like a permission problem and would
    /// be diagnosed as one.
    /// </summary>
    [Fact]
    public async Task ATokenAboutToExpireIsNotReused()
    {
        // Two minutes of life left, inside the renewal margin.
        var (source, handler, client) = CreateSource( r => RespondHappily( r, "2026-07-29T05:02:00Z" ) );

        using ( source )
        using ( client )
        {
            _ = await source.GetInstallationTokenAsync( "metalama", "Metalama" );
            _ = await source.GetInstallationTokenAsync( "metalama", "Metalama" );

            Assert.Equal( 4, handler.Requests.Count );
        }
    }

    /// <summary>
    /// Fifty-five minutes of reuse out of the hour GitHub issues, stated as a fact about the constant rather than
    /// inferred from the two tests above.
    /// </summary>
    [Fact]
    public void TheRenewalMarginLeavesFiftyFiveMinutesOfReuse()
        => Assert.Equal( TimeSpan.FromMinutes( 55 ), TimeSpan.FromHours( 1 ) - GitHubAppTokenSource.RenewalMargin );

    /// <summary>
    /// An app that is not installed on a repository fails naming that repository. This is the likeliest failure in
    /// practice, and the raw 404 says only "Not Found".
    /// </summary>
    [Fact]
    public async Task AnAppNotInstalledOnTheRepositoryFailsNamingIt()
    {
        var (source, handler, client) = CreateSource( _ => (HttpStatusCode.NotFound, """{"message":"Not Found"}""") );

        using ( source )
        using ( client )
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => source.GetInstallationTokenAsync( "someone-else", "Secret" ) );

            Assert.Contains( "someone-else/Secret", exception.Message, StringComparison.Ordinal );
            Assert.Contains( "404", exception.Message, StringComparison.Ordinal );
        }
    }

    /// <summary>
    /// The assertion authenticates as the application, and GitHub refuses one that lives longer than ten minutes or
    /// that claims to have been issued in the future. Both bounds are respected with room for a clock that disagrees.
    /// </summary>
    [Fact]
    public void TheAssertionIsSignedAndWithinTheBoundsGitHubAccepts()
    {
        var pem = CreatePrivateKeyPem();
        using var source = new GitHubAppTokenSource( new GitHubAppCredentials( "12345", pem ), null, () => _now );

        var assertion = source.CreateAssertion( _now );
        var parts = assertion.Split( '.' );

        Assert.Equal( 3, parts.Length );

        Assert.Equal( """{"alg":"RS256","typ":"JWT"}""", Encoding.UTF8.GetString( Base64UrlDecode( parts[0] ) ) );

        using var payload = JsonDocument.Parse( Base64UrlDecode( parts[1] ) );
        var issuedAt = payload.RootElement.GetProperty( "iat" ).GetInt64();
        var expiresAt = payload.RootElement.GetProperty( "exp" ).GetInt64();

        Assert.Equal( "12345", payload.RootElement.GetProperty( "iss" ).GetString() );
        Assert.True( issuedAt < _now.ToUnixTimeSeconds(), "The assertion is not backdated, so a fast clock would make GitHub refuse it." );
        Assert.True( expiresAt - issuedAt <= 600, "The assertion lives longer than the ten minutes GitHub accepts." );
        Assert.True( expiresAt > _now.ToUnixTimeSeconds(), "The assertion is already expired when it is created." );

        // The signature has to verify against the key, which is the half a hand-rolled JWT gets wrong.
        using var rsa = RSA.Create();
        rsa.ImportFromPem( pem );

        Assert.True(
            rsa.VerifyData(
                Encoding.UTF8.GetBytes( $"{parts[0]}.{parts[1]}" ),
                Base64UrlDecode( parts[2] ),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1 ) );
    }

    /// <summary>
    /// A key that cannot be read fails with a message describing what arrived, without quoting it. The key is a secret
    /// and the build log is not, but "it did not parse" alone leaves nobody able to tell a truncated secret from an
    /// unset one.
    /// </summary>
    [Fact]
    public void AnUnreadableKeyFailsWithoutQuotingIt()
    {
        using var source = new GitHubAppTokenSource( new GitHubAppCredentials( "12345", "not-a-key-at-all" ), null, () => _now );

        var exception = Assert.Throws<InvalidOperationException>( () => source.CreateAssertion( _now ) );

        Assert.Contains( "does not start", exception.Message, StringComparison.Ordinal );
        Assert.Contains( "16 characters", exception.Message, StringComparison.Ordinal );
        Assert.DoesNotContain( "not-a-key-at-all", exception.Message, StringComparison.Ordinal );
    }

    /// <summary>
    /// The three shapes a PEM key survives the journey to a container in. Base64 is the one to prefer, because
    /// <c>DockerBuild.ps1</c> writes forwarded variables into a generated PowerShell file and a single-line value has
    /// no line endings for anything along the way to rewrite.
    /// </summary>
    [Fact]
    public void APemKeyIsAcceptedAsItself_AsBase64_AndWithEscapedNewlines()
    {
        var pem = CreatePrivateKeyPem().ReplaceLineEndings( "\n" ).Trim();

        Assert.Equal( pem, GitHubAppCredentials.NormalizePrivateKey( pem ) );
        Assert.Equal( pem, GitHubAppCredentials.NormalizePrivateKey( Convert.ToBase64String( Encoding.UTF8.GetBytes( pem ) ) ) );

        var escaped = pem.Replace( "\n", "\\n", StringComparison.Ordinal );
        Assert.DoesNotContain( '\n', escaped );
        Assert.Equal( pem, GitHubAppCredentials.NormalizePrivateKey( escaped ) );
    }

    /// <summary>
    /// The variable a script reads back from the command's output file is derived from the owner, and a GitHub account
    /// name can hold characters an environment variable name cannot.
    /// </summary>
    [Fact]
    public void TheVariableNameIsDerivedFromTheOwner()
    {
        Assert.Equal( "GITHUB_TOKEN_METALAMA", GetGitHubAppTokenCommand.GetVariableName( "metalama" ) );
        Assert.Equal( "GITHUB_TOKEN_POSTSHARP_OPS", GetGitHubAppTokenCommand.GetVariableName( "postsharp-ops" ) );
    }

    private static byte[] Base64UrlDecode( string value )
    {
        var padded = value.Replace( '-', '+' ).Replace( '_', '/' );

        return Convert.FromBase64String( padded.PadRight( padded.Length + ((4 - (padded.Length % 4)) % 4), '=' ) );
    }

    /// <summary>
    /// Guards the plumbing rather than the code. The credentials reach the container only because the generated
    /// <c>DockerBuild.ps1</c> forwards every name in this list, so a name known to the token source but absent from the
    /// list would be silently unset at run time, and the failure would read as a missing app installation.
    /// </summary>
    [Fact]
    public void TheAppCredentialsAreForwardedToTheContainer()
    {
        Assert.Contains( EnvironmentVariableNames.GitHubAppId, EnvironmentVariableNames.All );
        Assert.Contains( EnvironmentVariableNames.GitHubAppPrivateKey, EnvironmentVariableNames.All );
    }
}
