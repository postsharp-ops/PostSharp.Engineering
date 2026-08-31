// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PostSharp.Engineering.BuildTools.Tools.GitHub;

/// <summary>
/// Mints GitHub App installation access tokens and writes them to a file as <c>NAME=VALUE</c> lines, one per repository
/// owner.
/// </summary>
/// <remarks>
/// <para>
/// This makes <see cref="GitHubAppTokenSource"/> reachable from a script, which is where it is usually needed: the
/// process that has to authenticate is <c>gh</c>, or a product tool, not the build engine. A caller reads the file,
/// sets the variables, and runs the tool.
/// </para>
/// <para>
/// <b>One token per owner, scoped to the repositories asked for.</b> GitHub issues a token per installation and an
/// installation belongs to one account, so repositories are grouped by owner and each group gets its own token, named
/// <c>GITHUB_TOKEN_&lt;OWNER&gt;</c>. That is the whole reason this exists next to the TeamCity build-scoped token: no
/// single build-scoped token can span two organizations, and a job that writes to both needs one for each.
/// </para>
/// <para>
/// <b>Written to a file rather than printed, deliberately.</b> A token is a credential; the standard output of a build
/// step is the build log, which is retained, searchable, and visible to everyone with read access to the project. The
/// default location is under <c>artifacts</c>, which is not committed. What does go to the console is the variable
/// name, what it reaches, and when it expires.
/// </para>
/// <para>
/// <b>A token lasts one hour and cannot be renewed</b>, so a script should call this immediately before using the
/// token, not at the start of a job that may run longer than that.
/// </para>
/// </remarks>
[UsedImplicitly]
internal class GetGitHubAppTokenCommand : BaseCommand<GetGitHubAppTokenSettings>
{
    protected override bool ExecuteCore( BuildContext context, GetGitHubAppTokenSettings settings )
    {
        var console = context.Console;

        console.WriteHeading( "Getting GitHub App installation tokens" );

        if ( !GitHubAppCredentials.TryGetFromEnvironment( out var credentials, out var error ) )
        {
            console.WriteError( error );

            return false;
        }

        var repositories = settings.Repositories;

        if ( repositories.Length == 0 )
        {
            if ( context.Product.DependencyDefinition.VcsRepository is not GitHubRepository ownRepository )
            {
                console.WriteError( "The current product is not in a GitHub repository, so --repository is required." );

                return false;
            }

            repositories = [$"{ownRepository.Owner}/{ownRepository.Name}"];
        }

        var byOwner = new SortedDictionary<string, SortedSet<string>>( StringComparer.OrdinalIgnoreCase );

        foreach ( var repository in repositories )
        {
            var parts = repository.Split( '/' );

            if ( parts.Length != 2 || parts.Any( string.IsNullOrWhiteSpace ) )
            {
                console.WriteError( $"'{repository}' is not a repository. Give it as owner/name." );

                return false;
            }

            if ( !byOwner.TryGetValue( parts[0], out var names ) )
            {
                names = new SortedSet<string>( StringComparer.OrdinalIgnoreCase );
                byOwner.Add( parts[0], names );
            }

            names.Add( parts[1] );
        }

        var outputFile = Path.GetFullPath(
            settings.OutputFile ?? Path.Combine( context.RepoDirectory, "artifacts", "github-app-tokens.env" ) );

        var lines = new StringBuilder();

        using var tokenSource = new GitHubAppTokenSource( credentials );

        foreach ( var (owner, names) in byOwner )
        {
            GitHubAppInstallationToken token;

            try
            {
                token = tokenSource
                    .GetInstallationTokenAsync( owner, [..names], context.CancellationToken )
                    .ConfigureAwait( false )
                    .GetAwaiter()
                    .GetResult();
            }
            catch ( Exception e )
            {
                console.WriteError( e.Message );

                return false;
            }

            var variableName = GetVariableName( owner );
            lines.AppendLine( CultureInfo.InvariantCulture, $"{variableName}={token.Token}" );

            console.WriteMessage(
                $"{variableName} reaches {string.Join( ", ", names.Select( n => $"{owner}/{n}" ) )} until {token.ExpiresOn:u}." );
        }

        var directory = Path.GetDirectoryName( outputFile );

        if ( directory != null )
        {
            Directory.CreateDirectory( directory );
        }

        var encoding = new UTF8Encoding( false );

        if ( settings.Append )
        {
            File.AppendAllText( outputFile, lines.ToString(), encoding );
        }
        else
        {
            File.WriteAllText( outputFile, lines.ToString(), encoding );
        }

        console.WriteSuccess( $"Tokens written to '{outputFile}'. They expire in an hour and cannot be renewed." );

        return true;
    }

    /// <summary>
    /// <c>GITHUB_TOKEN_&lt;OWNER&gt;</c>, with anything an environment variable name cannot hold replaced. GitHub
    /// account names allow hyphens, which a shell would read as an operator in some of the places these are used.
    /// </summary>
    internal static string GetVariableName( string owner )
        => "GITHUB_TOKEN_" + new string( owner.Select( c => char.IsLetterOrDigit( c ) ? char.ToUpperInvariant( c ) : '_' ).ToArray() );
}
