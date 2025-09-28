// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Octokit;
using Octokit.GraphQL;
using Octokit.GraphQL.Core;
using Octokit.GraphQL.Model;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Connection = Octokit.GraphQL.Connection;
using Environment = System.Environment;
using ProductHeaderValue = Octokit.ProductHeaderValue;
using PullRequestMergeMethod = Octokit.GraphQL.Model.PullRequestMergeMethod;
using PullRequestReviewEvent = Octokit.PullRequestReviewEvent;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

public static class GitHubHelper
{
    private const string _productHeaderName = "PostSharp.Engineering";
    private static readonly string _productHeaderVersion = typeof(GitHubHelper).Assembly.GetName().Version!.ToString();

    internal static bool TryGetToken(
        ConsoleHelper console,
        [NotNullWhen( true )] out string? token,
        string tokenEnvironmentVariableName = EnvironmentVariableNames.GitHubToken )
        => TryGetToken( console, true, out token );

    internal static bool TryGetToken(
        ConsoleHelper console,
        bool writeError,
        [NotNullWhen( true )] out string? token,
        string tokenEnvironmentVariableName = EnvironmentVariableNames.GitHubToken )
    {
        token = Environment.GetEnvironmentVariable( tokenEnvironmentVariableName );

        if ( string.IsNullOrEmpty( token ) )
        {
            if ( writeError )
            {
                console.WriteError( $"The '{tokenEnvironmentVariableName}' environment variable is not defined." );
            }

            token = null;

            return false;
        }

        return true;
    }

    private static GitHubClient ConnectRestApi( string token )
        => new( new ProductHeaderValue( _productHeaderName, _productHeaderVersion ) ) { Credentials = new Credentials( token ) };

    private static bool TryConnectRestApi(
        ConsoleHelper console,
        [NotNullWhen( true )] out GitHubClient? client,
        string tokenEnvironmentVariableName = EnvironmentVariableNames.GitHubToken )
    {
        if ( !TryGetToken( console, out var token, tokenEnvironmentVariableName ) )
        {
            client = null;

            return false;
        }

        client = ConnectRestApi( token );

        return true;
    }

    private static bool TryConnectGraphQl( ConsoleHelper console, [NotNullWhen( true )] out Connection? connection )
    {
        if ( !TryGetToken( console, out var token ) )
        {
            connection = null;

            return false;
        }

        connection = new Connection( new Octokit.GraphQL.ProductHeaderValue( _productHeaderName, _productHeaderVersion ), token );

        return true;
    }

    public static bool TryDownloadText( ConsoleHelper console, GitHubRepository repository, string path, string branch, [NotNullWhen( true )] out string? text )
    {
        if ( !TryConnectRestApi( console, out var gitHub ) )
        {
            text = null;

            return false;
        }

        var raw = gitHub.Repository.Content.GetRawContentByRef( repository.Owner, repository.Name, path, branch ).GetAwaiter().GetResult();
        text = Encoding.UTF8.GetString( raw );

        return true;
    }

    public static async Task<(bool Success, string? Url, bool RequiresBuild)> TryCreatePullRequestAsync(
        ConsoleHelper console,
        GitHubRepository repository,
        string sourceBranch,
        string targetBranch,
        string title )
    {
        bool TryConnectRestApis( [NotNullWhen( true )] out GitHubClient? creatorClient, out GitHubClient? reviewerClient )
        {
            creatorClient = null;
            reviewerClient = null;

            if ( !TryGetToken( console, out var creatorToken ) )
            {
                return false;
            }

            creatorClient = ConnectRestApi( creatorToken );

            if ( !TryGetToken( console, false, out var reviewerToken, EnvironmentVariableNames.GitHubReviewerToken ) )
            {
                console.WriteWarning(
                    $"The {EnvironmentVariableNames.GitHubReviewerToken} environment variable is not defined. The PR won't be auto-approved." );
            }
            else
            {
                reviewerClient = creatorToken == reviewerToken ? creatorClient : ConnectRestApi( reviewerToken );
            }

            return true;
        }

        if ( !TryConnectRestApis( out var creatorGitHub, out var reviewerGitHub ) )
        {
            return default;
        }

        if ( !TryConnectGraphQl( console, out var graphQl ) )
        {
            return default;
        }

        var allExistingPullRequests = await creatorGitHub.PullRequest.GetAllForRepository( repository.Owner, repository.Name );
        var pullRequest = allExistingPullRequests.FirstOrDefault( pr => pr.Head.Ref == sourceBranch );

        if ( pullRequest != null )
        {
            console.WriteMessage( $"Existing PR found: {pullRequest.Url}." );
        }
        else
        {
            console.WriteMessage( "Creating pull request." );
            var newPullRequest = new NewPullRequest( title, sourceBranch, targetBranch );
            pullRequest = await creatorGitHub.PullRequest.Create( repository.Owner, repository.Name, newPullRequest );
            console.WriteMessage( $"Pull request created: {pullRequest.Url}" );
        }

        // A pull request cannot be self-reviewed on GitHub.
        // https://github.com/orgs/community/discussions/6292
        if ( reviewerGitHub != null )
        {
            var reviewerLogin = reviewerGitHub.User.Current().Result.Login;
            console.WriteMessage( $"Requesting a review of the pull request from '{reviewerLogin}' user." );
            var reviewRequest = new PullRequestReviewRequest( new List<string> { reviewerLogin }, new List<string>() );
            pullRequest = await reviewerGitHub.PullRequest.ReviewRequest.Create( repository.Owner, repository.Name, pullRequest.Number, reviewRequest );

            console.WriteMessage( "Approving the pull request." );
            var pullRequestApproval = new PullRequestReviewCreate { Event = PullRequestReviewEvent.Approve };
            _ = await reviewerGitHub.PullRequest.Review.Create( repository.Owner, repository.Name, pullRequest.Number, pullRequestApproval );
        }

        // Check if the PR is in a clean state before enabling auto-merge.
        // Connect to REST API to get latest PR details.
        if ( !TryConnectRestApi( console, out var restClient ) )
        {
            console.WriteError( "Could not connect to GitHub REST API." );

            return default;
        }

        // Wait until the mergeability is known.
        var waitStatusStopwatch = Stopwatch.StartNew();

        while ( pullRequest.Mergeable == null && waitStatusStopwatch.Elapsed < TimeSpan.FromMinutes( 1 ) )
        {
            console.WriteMessage( $"Waiting until we know whether the PR is mergeable (waited {waitStatusStopwatch.Elapsed} so far)." );
            Thread.Sleep( 200 );
            pullRequest = await creatorGitHub.PullRequest.Get( repository.Owner, repository.Name, pullRequest.Number );
        }

        // Check status checks (if any required)
        if ( pullRequest.Mergeable == true )
        {
            console.WriteMessage( "PR is in a clean state. Merging directly." );

            // Directly merge the PR.
            var mergeResult = await restClient.PullRequest.Merge(
                repository.Owner,
                repository.Name,
                pullRequest.Number,
                new MergePullRequest { CommitTitle = title, MergeMethod = Octokit.PullRequestMergeMethod.Merge } );

            if ( mergeResult.Merged )
            {
                console.WriteMessage( "PR merged successfully." );
                var url = $"https://github.com/{repository.Owner}/{repository.Name}/pull/{pullRequest.Number}";

                return (true, url, false);
            }
            else
            {
                console.WriteError( "Failed to merge PR directly. Proceeding to enable auto-merge." );
            }
        }

        // Enable auto-merge.
        console.WriteMessage( "PR is not in a clean state. Enabling pull request auto-merge." );

        var pullRequestQuery = new Query()
            .RepositoryOwner( repository.Owner )
            .Repository( repository.Name )
            .Select( repo => repo.PullRequest( pullRequest.Number ) )
            .Select( pr => pr.Id )
            .Compile();

        var pullRequestId = await graphQl.Run( pullRequestQuery );

        var authorEmail = Environment.GetEnvironmentVariable( EnvironmentVariableNames.GitHubAuthorEmail ) ?? "teamcity@postsharp.net";

        var enableAutoMergeMutation = new Mutation()
            .EnablePullRequestAutoMerge(
                new Arg<EnablePullRequestAutoMergeInput>(
                    new EnablePullRequestAutoMergeInput
                    {
                        AuthorEmail = authorEmail, CommitHeadline = title, MergeMethod = PullRequestMergeMethod.Merge, PullRequestId = pullRequestId
                    } ) )
            .Select( am => am.ClientMutationId )
            .Compile();

        _ = await graphQl.Run( enableAutoMergeMutation );

        var finalUrl = $"https://github.com/{repository.Owner}/{repository.Name}/pull/{pullRequest.Number}";

        return (true, finalUrl, true);
    }

    public static async Task<bool> TrySetBranchPoliciesAsync(
        BuildContext context,
        GitHubRepository gitHubRepository,
        string buildStatusGenre,
        string? buildStatusName,
        bool dry )
    {
        if ( !TryConnectGraphQl( context.Console, out var graphQl ) )
        {
            return false;
        }

        var branch = context.Product.DependencyDefinition.Branch;

        context.Console.WriteMessage( $"Setting protection rule for '{branch}' branch." );

        var repositoryIdQuery = new Query()
            .RepositoryOwner( gitHubRepository.Owner )
            .Repository( gitHubRepository.Name )
            .Select( r => r.Id )
            .Compile();

        var repositoryId = await graphQl.Run( repositoryIdQuery );

        var ruleMutation = new Mutation()
            .CreateBranchProtectionRule(
                new Arg<CreateBranchProtectionRuleInput>(
                    new CreateBranchProtectionRuleInput
                    {
                        RepositoryId = repositoryId,
                        Pattern = branch,
                        RequiresApprovingReviews = true,
                        RequiredApprovingReviewCount = 1,
                        RequiresStatusChecks = buildStatusName != null,

                        // Don't require the branch to be up to date.
                        RequiresStrictStatusChecks = false,
                        RequiredStatusChecks =
                            buildStatusName == null
                                ? []
                                : new[] { new RequiredStatusCheckInput { Context = $"{buildStatusGenre}/{buildStatusName}" } },
                        RequiresConversationResolution = true
                    } ) )
            .Select( r => r.BranchProtectionRule ) // We need to select something to avoid ResponseDeserializerException
            .Select( r => r.Pattern )
            .Compile();

        if ( !dry )
        {
            _ = await graphQl.Run( ruleMutation );
        }

        branch = context.Product.DependencyDefinition.ReleaseBranch;

        if ( branch != null )
        {
            context.Console.WriteMessage( $"Setting protection rule for '{branch}' branch." );

            ruleMutation = new Mutation()
                .CreateBranchProtectionRule(
                    new Arg<CreateBranchProtectionRuleInput>(
                        new CreateBranchProtectionRuleInput
                        {
                            RepositoryId = repositoryId,
                            Pattern = branch,
                            RequiresApprovingReviews = true,
                            RequiredApprovingReviewCount = 1,
                            RequiresConversationResolution = true
                        } ) )
                .Select( r => r.BranchProtectionRule ) // We need to select something to avoid ResponseDeserializerException
                .Select( r => r.Pattern )
                .Compile();

            if ( !dry )
            {
                _ = await graphQl.Run( ruleMutation );
            }
        }

        return true;
    }

    public static async Task<bool> TryPrintBranchPoliciesAsync(
        BuildContext context,
        GitHubRepository gitHubRepository )
    {
        if ( !TryConnectGraphQl( context.Console, out var graphQl ) )
        {
            return false;
        }

        // GraphQL requires explicit list of properties.
        // This loop is used to list the properties for the query bellow.
        foreach ( var property in typeof(BranchProtectionRule).GetProperties() )
        {
            if ( property.PropertyType != typeof(bool) && property.PropertyType != typeof(string) )
            {
                continue;
            }

            context.Console.WriteMessage( $"r.{property.Name}," );
        }

        context.Console.WriteMessage( $"" );

        context.Console.WriteMessage( $"Getting protection rules." );

        var branchProtectionRulesQuery = new Query()
            .RepositoryOwner( gitHubRepository.Owner )
            .Repository( gitHubRepository.Name )
            .BranchProtectionRules()
            .AllPages()
            .Select( r => new
            {
                // For this code, use the output of the loop above.
                r.AllowsDeletions,
                r.AllowsForcePushes,
                r.BlocksCreations,
                r.DismissesStaleReviews,
                r.IsAdminEnforced,
                r.Pattern,
                r.RequiresApprovingReviews,
                r.RequiresCodeOwnerReviews,
                r.RequiresCommitSignatures,
                r.RequiresConversationResolution,
                r.RequiresLinearHistory,
                r.RequiresStatusChecks,
                r.RequiresStrictStatusChecks,
                r.RestrictsPushes,
                r.RestrictsReviewDismissals
            } )
            .Compile();

        var rules = await graphQl.Run( branchProtectionRulesQuery );

        context.Console.WriteMessage( "" );

        var i = 0;

        foreach ( var rule in rules )
        {
            context.Console.WriteMessage( $"{i++}:" );

            foreach ( var property in rule.GetType().GetProperties() )
            {
                context.Console.WriteMessage( $"{property.Name}: {property.GetValue( rule )}" );
            }

            context.Console.WriteMessage( "" );
        }

        return true;
    }

    public static async Task<bool> TrySetDefaultBranchAsync(
        ConsoleHelper console,
        GitHubRepository gitHubRepository,
        string defaultBranch,
        bool dry )
    {
        console.WriteMessage( $"Setting repository default branch to '{defaultBranch}'." );

        if ( !TryConnectRestApi( console, out var gitHub ) )
        {
            return false;
        }

        var repositoryUpdate = new RepositoryUpdate() { DefaultBranch = defaultBranch };

        if ( !dry )
        {
            _ = await gitHub.Repository.Edit( gitHubRepository.Owner, gitHubRepository.Name, repositoryUpdate );
        }

        return true;
    }
}