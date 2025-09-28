// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

public static class AzureDevOpsHelper
{
    private static bool TryConnect( ConsoleHelper console, string url, [NotNullWhen( true )] out VssConnection? connection )
    {
        var user = Environment.GetEnvironmentVariable( EnvironmentVariableNames.AzureDevOpsUser ) ?? "teamcity@postsharp.net";

        var token = Environment.GetEnvironmentVariable( EnvironmentVariableNames.AzureDevOpsToken );

        if ( string.IsNullOrEmpty( token ) )
        {
            console.WriteError( $"The '{EnvironmentVariableNames.AzureDevOpsToken}' environment variable is not defined." );
            connection = null;

            return false;
        }

        var credentials = new VssBasicCredential( user, token );
        connection = new VssConnection( new Uri( url ), credentials );

        return true;
    }

    // https://github.com/microsoft/azure-devops-dotnet-samples/blob/main/ClientLibrary/Samples/Git/PullRequestsSample.cs
    // https://stackoverflow.com/a/52025418/4100001
    // Required personal access token scopes: Code: Read&Write
    public static async Task<(bool Success, string? Url, bool RequiresBuild)> TryCreatePullRequestAsync(
        ConsoleHelper console,
        AzureDevOpsRepository repository,
        string sourceBranch,
        string targetBranch,
        string title )
    {
        try
        {
            if ( !TryConnect( console, repository.BaseUrl, out var azureDevOps ) )
            {
                return default;
            }

            using ( azureDevOps )
            {
                using var azureDevOpsGit = azureDevOps.GetClient<GitHttpClient>();

                var pullRequest = new GitPullRequest()
                {
                    SourceRefName = $"refs/heads/{sourceBranch}", TargetRefName = $"refs/heads/{targetBranch}", Title = title
                };

                console.WriteMessage( "Creating a pull request." );
                var createdPullRequest = await azureDevOpsGit.CreatePullRequestAsync( pullRequest, repository.Project, repository.Name );

                console.WriteMessage( "Approving the pull request." );

                _ = await azureDevOpsGit.CreatePullRequestReviewerAsync(
                    new IdentityRefWithVote( new IdentityRef { Id = createdPullRequest.CreatedBy.Id } ) { Vote = 10 },
                    repository.Project,
                    repository.Name,
                    createdPullRequest.PullRequestId,
                    createdPullRequest.CreatedBy.Id );

                var pullRequestWithAutoCompleteEnabled = new GitPullRequest
                {
                    AutoCompleteSetBy = new IdentityRef { Id = createdPullRequest.CreatedBy.Id },
                    CompletionOptions = new GitPullRequestCompletionOptions { DeleteSourceBranch = true, MergeCommitMessage = title }
                };

                console.WriteMessage( "Setting the new pull request to get completed automatically." );

                _ = await azureDevOpsGit.UpdatePullRequestAsync(
                    pullRequestWithAutoCompleteEnabled,
                    repository.Project,
                    repository.Name,
                    createdPullRequest.PullRequestId );

                var url = $"{repository.BaseUrl}/{repository.Project}/_git/{repository.Name}/pullrequest/{createdPullRequest.PullRequestId}";

                return (true, url, true);
            }
        }
        catch ( Exception e )
        {
            console.WriteError( e.ToString() );

            return default;
        }
    }

    public static async Task<bool> TrySetBranchPoliciesAsync(
        BuildContext context,
        AzureDevOpsRepository azureDevOpsRepository,
        string buildStatusGenre,
        string? buildStatusName,
        bool dry )
    {
        // Error message "The update is rejected by policy." usually means that a policy already exists.

        var repository = azureDevOpsRepository.Name;
        var org = azureDevOpsRepository.BaseUrl;
        var project = azureDevOpsRepository.Project;
        var projectIdArgs = $"--org {org} --project {project}";

        var console = context.Console;
        var product = context.Product;

        console.WriteMessage( "Fetching repository ID." );

        if ( !AzHelper.Query(
                context,
                $"repos show --repository {repository} {projectIdArgs}",
                dry,
                out var repositoryJson ) )
        {
            return false;
        }

        var repositoryId = dry ? Guid.Empty : JsonDocument.Parse( repositoryJson ).RootElement.GetProperty( "id" ).GetGuid();
        var repositoryIdArgs = $"{projectIdArgs} --repository-id {repositoryId}";

        string GetCommonArgs( string branch )
        {
            var branchIdArgs = $"{repositoryIdArgs} --branch {branch} --branch-match-type exact";

            return $"{branchIdArgs} --blocking true --enabled true";
        }

        var branch = product.DependencyDefinition.Branch;
        var commonArgs = GetCommonArgs( branch );

        bool TryRequireApproversAndCommentResolution()
        {
            console.WriteMessage( $"Requiring approvers for '{branch}' branch." );

            if ( !AzHelper.Run(
                    context,
                    $"repos policy approver-count create {commonArgs} --allow-downvotes false --creator-vote-counts true --minimum-approver-count 1 --reset-on-source-push false",
                    dry ) )
            {
                return false;
            }

            console.WriteMessage( $"Requiring comment resolution for '{branch}' branch." );

            if ( !AzHelper.Run( context, $"repos policy comment-required create {commonArgs}", dry ) )
            {
                return false;
            }

            return true;
        }

        if ( !TryRequireApproversAndCommentResolution() )
        {
            return false;
        }

        if ( buildStatusName == null )
        {
            console.WriteMessage( $"Success status for '{branch}' branch is not required." );
        }
        else
        {
            console.WriteMessage( $"Requiring success status for '{branch}' branch." );

            // This is not covered by "az repos policy" command. https://github.com/Azure/azure-devops-cli-extension/issues/1040
            var statusCheckPayload = $@"{{
  ""isBlocking"": true,
  ""isEnabled"": true,
  ""settings"": {{
    ""authorId"": null,
    ""defaultDisplayName"": ""Build [Debug]"",
    ""invalidateOnSourceUpdate"": true,
    ""policyApplicability"": null,
    ""scope"": [
      {{
        ""matchKind"": ""Exact"",
        ""refName"": ""refs/heads/{branch}"",
        ""repositoryId"": ""{repositoryId}""
      }}
    ],
    ""statusGenre"": ""{buildStatusGenre}"",
    ""statusName"": ""{buildStatusName}""
  }},
  ""type"": {{
    ""displayName"": ""Status"",
    ""id"": ""cbdc66da-9728-4af8-aada-9a5a32e4a226""
  }}
}}";

            var statusCheckPayloadFile = Path.GetTempFileName();

            try
            {
                await File.WriteAllTextAsync( statusCheckPayloadFile, statusCheckPayload );

                if ( !AzHelper.Run( context, $"repos policy create {projectIdArgs} --config {statusCheckPayloadFile}", dry ) )
                {
                    return false;
                }
            }
            finally
            {
                File.Delete( statusCheckPayloadFile );
            }
        }

        if ( product.DependencyDefinition.ReleaseBranch != null )
        {
            var developBranch = branch;
            branch = product.DependencyDefinition.ReleaseBranch;
            commonArgs = GetCommonArgs( branch );

            if ( !TryRequireApproversAndCommentResolution() )
            {
                return false;
            }

            console.WriteMessage( $"Requiring reviewers for '{branch}' branch." );

            var message =
                $"\"TeamCity is a required reviewer because only automated merges during publishing are allowed to a release branch. For development, use '{developBranch}' branch as a target branch of your PR.\"";

            var options = new ToolInvocationOptions( new Dictionary<string, string?> { { "ReviewerMessage", message } }.ToImmutableDictionary() );

            if ( !AzHelper.Run(
                    context,
                    $"repos policy required-reviewer create {commonArgs} --message %ReviewerMessage% --required-reviewer-ids teamcity@postsharp.net",
                    dry,
                    options ) )
            {
                return false;
            }
        }

        return await Task.FromResult( true );
    }

    public static async Task<bool> TrySetDefaultBranchAsync(
        BuildContext context,
        AzureDevOpsRepository azureDevOpsRepository,
        string defaultBranch,
        bool dry )
    {
        var repository = azureDevOpsRepository.Name;
        var org = azureDevOpsRepository.BaseUrl;
        var project = azureDevOpsRepository.Project;
        var projectIdArgs = $"--org {org} --project {project}";
        var repositoryIdArgs = $"{projectIdArgs} --repository {repository}";

        context.Console.WriteMessage( $"Setting repository default branch to '{defaultBranch}'." );

        if ( !AzHelper.Run(
                context,
                $"repos update {repositoryIdArgs} --default-branch {defaultBranch}",
                dry ) )
        {
            return false;
        }

        return await Task.FromResult( true );
    }
}