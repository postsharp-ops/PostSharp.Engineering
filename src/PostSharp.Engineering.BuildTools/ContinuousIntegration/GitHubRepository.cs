// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

public class GitHubRepository : VcsRepository
{
    // E.g.
    // git@github.com:postsharp/Metalama.Documentation.git // Used on TeamCity
    // https://github.com/postsharp/Metalama.Documentation.git // Used locally
    private static readonly Regex _urlRegex = new( "^(?:git@github.com:|https://github.com/)(?<owner>[^/]+)/(?<repo>[^/]+)\\.git$" );

    public override string Name { get; }

    public override VcsProvider Provider => VcsProvider.GitHub;

    public string Owner { get; }

    public override string SshUrl => $"git@github.com:{this.Owner}/{this.Name}.git";

    public override string HttpUrl => $"https://github.com/{this.Owner}/{this.Name}.git";

    public override string DeveloperMachineRemoteUrl => this.HttpUrl;

    public override string TeamCityRemoteUrl => this.SshUrl;

    public override bool IsSshAgentRequired => false;

    /// <summary>
    /// Gets the name of the environment variable that carries the token of this repository. It is the variable named
    /// after the owner when that variable is set, and the ordinary <c>GITHUB_TOKEN</c> otherwise.
    /// </summary>
    /// <remarks>
    /// A build that writes to a single GitHub organization receives its token in <c>GITHUB_TOKEN</c>. A build that also
    /// writes to another organization receives a second token, because a token belongs to one GitHub App installation
    /// and an installation to one account, and that second token arrives in the variable named after its organization.
    /// The environment is read here so that both cases go through the same code path: the variable named after the
    /// owner exists only where a token was issued for that owner.
    /// </remarks>
    public override string TokenEnvironmentVariableName
    {
        get
        {
            var ownerVariableName = GetTokenEnvironmentVariableName( this.Owner );

            return string.IsNullOrEmpty( Environment.GetEnvironmentVariable( ownerVariableName ) )
                ? EnvironmentVariableNames.GitHubToken
                : ownerVariableName;
        }
    }

    /// <summary>
    /// Gets the name of the environment variable that carries the token of <paramref name="owner"/>:
    /// <c>GITHUB_TOKEN_&lt;OWNER&gt;</c>, with every character that an environment variable name cannot hold replaced
    /// by an underscore. GitHub account names allow hyphens, which a shell would read as an operator.
    /// </summary>
    public static string GetTokenEnvironmentVariableName( string owner )
        => EnvironmentVariableNames.GitHubToken
           + "_"
           + new string( owner.Select( c => char.IsLetterOrDigit( c ) ? char.ToUpperInvariant( c ) : '_' ).ToArray() );

    public GitHubRepository( string name, string owner, string? defaultBranchParameter = null )
    {
        this.Name = name;
        this.Owner = owner;
    }

    public static bool TryParse( string repoUrl, [NotNullWhen( true )] out GitHubRepository? repository )
    {
        var match = _urlRegex.Match( repoUrl );

        if ( !match.Success )
        {
            repository = null;

            return false;
        }

        var owner = match.Groups["owner"].Value;
        var name = match.Groups["repo"].Value;
        repository = new GitHubRepository( name, owner );

        return true;
    }

    public override bool TryDownloadTextFile( ConsoleHelper console, string branch, string path, [NotNullWhen( true )] out string? text )
        => GitHubHelper.TryDownloadText( console, this, path, branch, out text );

    public override Task<bool> TrySetBranchPoliciesAsync( BuildContext context, string buildStatusGenre, string? buildStatusName, bool dry )
        => GitHubHelper.TrySetBranchPoliciesAsync( context, this, buildStatusGenre, buildStatusName, dry );

    public override Task<(bool Success, string? Url, bool RequiresBuild)> TryCreatePullRequestAsync(
        ConsoleHelper console,
        string sourceBranch,
        string targetBranch,
        string title,
        string? body = null )
        => GitHubHelper.TryCreatePullRequestAsync( console, this, sourceBranch, targetBranch, title, body );
}