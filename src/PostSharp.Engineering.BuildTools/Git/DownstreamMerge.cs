// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Git;

internal static class DownstreamMerge
{
    public static bool CheckUpstreamChanges( BuildContext context, BaseBuildSettings settings )
    {
        context.Console.WriteHeading( "Checking for pending upstream changes" );

        if ( !TryCheckPendingMerges( context, settings ) )
        {
            return false;
        }

        if ( !TryCheckUnmergedCommits( context, settings ) )
        {
            return false;
        }

        context.Console.WriteSuccess( settings.Force ? "Pending upstream changes check completed." : "No pending upstream changes found." );

        return true;
    }

    private static bool TryCheckUnmergedCommits( BuildContext context, BaseBuildSettings settings )
    {
        var upstreamProductFamily = context.Product.ProductFamily.UpstreamProductFamily;

        while ( upstreamProductFamily != null )
        {
            context.Console.WriteMessage( $"Checking '{context.Product.ProductName}' product version '{upstreamProductFamily.Version}' for unmerged changes." );

            if ( !upstreamProductFamily.TryGetDependencyDefinition( context.Product.ProductName, out var upstreamDependencyDefinition ) )
            {
                context.Console.WriteWarning(
                    $"The '{context.Product.ProductName}' upstream product version '{upstreamProductFamily.Version}' is not configured. Assuming it didn't exists in this family version." );

                break;
            }

            var upstreamBranch = upstreamDependencyDefinition.Branch;

            if ( !GitHelper.TryFetch( context, upstreamBranch ) )
            {
                return false;
            }

            var remoteUpstreamBranch = $"remotes/origin/{upstreamBranch}";

            if ( !GitHelper.TryGetCommitsCount( context, "HEAD", remoteUpstreamBranch, upstreamProductFamily, out var commitsCount ) )
            {
                return false;
            }

            if ( commitsCount > 0 )
            {
                var message =
                    $"There are unmerged changes from the '{context.Product.ProductName}' upstream product version '{upstreamProductFamily.Version}'.";

                if ( settings.Force )
                {
                    context.Console.WriteWarning( $"{message} Ignoring." );
                }
                else
                {
                    context.Console.WriteError( $"{message} Run the related downstream merge or use --force." );

                    return false;
                }
            }

            upstreamProductFamily = upstreamProductFamily.UpstreamProductFamily;
        }

        return true;
    }

    private static bool TryCheckPendingMerges( BuildContext context, BaseBuildSettings settings )
    {
        var productFamily = context.Product.ProductFamily;
        var pendingBranchesExist = false;

        while ( productFamily != null )
        {
            context.Console.WriteMessage( $"Checking '{context.Product.ProductName}' product version '{productFamily.Version}' for pending merge branches." );

            var filter = $"merge/{productFamily.Version}/*";

            if ( !GitHelper.TryGetRemoteReferences( context, settings, filter, out var references ) )
            {
                return false;
            }

            if ( references.Length > 0 )
            {
                ExplainUnmergedBranches(
                    context.Console,
                    references.Select( r => r.Reference ),
                    settings.Force );

                pendingBranchesExist = true;
            }

            productFamily = productFamily.UpstreamProductFamily;
        }

        if ( settings.Force )
        {
            return true;
        }

        if ( pendingBranchesExist )
        {
            return false;
        }
        else
        {
            return true;
        }
    }

    public static bool MergeDownstream( BuildContext context, DownstreamMergeSettings settings )
    {
        // When on TeamCity, Git user credentials are set to TeamCity.
        if ( TeamCityHelper.IsTeamCityBuild( settings ) )
        {
            if ( !TeamCityHelper.TrySetGitIdentityCredentials( context ) )
            {
                return false;
            }
        }

        if ( !GitHelper.TryGetStatus( context, context.RepoDirectory, out var statuses ) )
        {
            return false;
        }

        if ( statuses.Length > 0 )
        {
            context.Console.WriteError( "The repository needs to be clean before running the downstream merge." );

            return false;
        }

        var sourceProductFamily = context.Product.ProductFamily;
        var downstreamProductFamily = context.Product.ProductFamily.DownstreamProductFamily;

        if ( downstreamProductFamily == null )
        {
            context.Console.WriteWarning(
                $"The downstream version product family for '{context.Product.ProductFamily.Version}' is not configured. Skipping downstream merge." );

            return true;
        }

        var sourceBranch = context.Product.DependencyDefinition.Branch;

        if ( !downstreamProductFamily.TryGetDependencyDefinition( context.Product.ProductName, out var downstreamDependencyDefinition ) )
        {
            context.Console.WriteError(
                $"The '{context.Product.ProductName}' downstream product version '{downstreamProductFamily.Version}' is not configured." );

            return false;
        }

        var downstreamBranch = downstreamDependencyDefinition.Branch;

        if ( context.Branch != sourceBranch )
        {
            context.Console.WriteError(
                $"Downstream merge can only be executed on the development branch ('{sourceBranch}'). The current branch is '{context.Branch}'." );

            return false;
        }

        context.Console.WriteHeading( $"Executing downstream merge from '{sourceBranch}' branch to '{downstreamBranch}' branch" );

        if ( !GitHelper.TryGetCurrentCommitHash( context, out var sourceCommitHash ) )
        {
            return false;
        }

        context.Console.WriteImportantMessage( $"Pulling changes from '{downstreamBranch}' downstream branch" );

        if ( !GitHelper.TryCheckoutAndPull( context, downstreamBranch ) )
        {
            return false;
        }

        if ( !GitHelper.TryGetCommitsCount( context, "HEAD", sourceBranch, sourceProductFamily, out var commitsCount ) )
        {
            return false;
        }

        if ( commitsCount < 0 )
        {
            throw new InvalidOperationException( $"Invalid commits count: {commitsCount}" );
        }

        if ( commitsCount == 0 )
        {
            context.Console.WriteSuccess( $"There are no commits to merge from '{sourceBranch}' branch to '{downstreamBranch}' branch." );

            return true;
        }

        context.Console.WriteImportantMessage( $"There are {commitsCount} commits to merge from '{sourceBranch}' branch to '{downstreamBranch}' branch." );

        var pullRequestStatusCheckBuildTypeId = downstreamDependencyDefinition.CiConfiguration.PullRequestStatusCheckBuildType;
        var isPullRequestRequired = pullRequestStatusCheckBuildTypeId != null;
        string targetBranch;
        bool targetBranchExistsRemotely;

        if ( isPullRequestRequired )
        {
            targetBranch = $"merge/{downstreamProductFamily.Version}/{context.Product.ProductFamily.Version}-{sourceCommitHash}";

            context.Console.WriteMessage(
                $"Checking '{context.Product.ProductName}' product version '{downstreamProductFamily.Version}' for pending merge branches." );

            var filter = $"merge/{downstreamProductFamily.Version}/*";

            if ( !GitHelper.TryGetRemoteReferences( context, settings, filter, out var references ) )
            {
                return false;
            }

            var targetBranchReference = $"refs/heads/{targetBranch}";

            targetBranchExistsRemotely = references.Any( r => r.Reference == targetBranchReference );

            var formerTargetBranchReferences = references.Where( r => r.Reference != targetBranchReference ).ToArray();

            if ( formerTargetBranchReferences.Length > 0 && !settings.Force )
            {
                ExplainUnmergedBranches(
                    context.Console,
                    formerTargetBranchReferences.Select( r => r.Reference ),
                    settings.Force,
                    targetBranchExistsRemotely
                        ? $"Until a new commit is pushed to the '{sourceBranch}' source branch, there's no need to delete the '{targetBranch}' target branch, as it will be reused next time the downstream merge is run."
                        : null );

                if ( !settings.Force )
                {
                    return false;
                }
            }
        }
        else
        {
            targetBranch = downstreamBranch;
            targetBranchExistsRemotely = true;
        }

        bool targetBranchExists;

        if ( targetBranchExistsRemotely )
        {
            targetBranchExists = true;
        }
        else
        {
            if ( !GitHelper.TryGetCurrentCommitHash( context, targetBranch, out var targetBranchCurrentCommitHash ) )
            {
                return false;
            }

            targetBranchExists = targetBranchCurrentCommitHash != null;
        }

        if ( targetBranchExists )
        {
            context.Console.WriteImportantMessage( $"The '{targetBranch}' target branch already exists. Let's use it." );

            if ( !GitHelper.TryCheckoutAndPull( context, targetBranch ) )
            {
                return false;
            }
        }
        else
        {
            context.Console.WriteImportantMessage( $"The '{targetBranch}' target branch doesn't exits. Let's create it." );

            if ( !GitHelper.TryCreateBranch( context, targetBranch ) )
            {
                return false;
            }

            context.Console.WriteImportantMessage( $"The '{targetBranch}' target was created." );
        }

        // Push the branch now to avoid issues when the DownstreamMergeCommand
        // is executed again with the same upstream changes
        // or when developers are required to resolve conflicts.
        if ( !GitHelper.TryPush( context, settings ) )
        {
            return false;
        }

        if ( !TryMerge( context, settings, sourceBranch, targetBranch, downstreamBranch, out var areChangesPending ) )
        {
            return false;
        }

        if ( !areChangesPending )
        {
            // This shouldn't happen often - just when the merge conflict is solved without using the merge branch prepared by the tool.
            context.Console.WriteSuccess( $"There is nothing to merge from '{sourceBranch}' branch to '{downstreamBranch}' branch." );

            return true;
        }

        context.Console.WriteSuccess( $"Changes from '{sourceBranch}' missing in '{downstreamBranch}' branch have been merged in branch '{targetBranch}'." );

        if ( isPullRequestRequired )
        {
            if ( !TryCreatePullRequest( context, targetBranch, downstreamBranch, sourceBranch, out var pullRequestUrl ) )
            {
                return false;
            }

            context.Console.WriteSuccess( $"Created pull request: {pullRequestUrl}" );

            if ( !TryScheduleBuild(
                    downstreamDependencyDefinition.CiConfiguration,
                    context.Console,
                    targetBranch,
                    sourceBranch,
                    pullRequestUrl,
                    pullRequestStatusCheckBuildTypeId!, // Checked by isPullRequestRequired
                    out var buildUrl ) )
            {
                return false;
            }

            context.Console.WriteSuccess( $"Scheduled build: {buildUrl}" );
        }

        return true;
    }

    private static bool TryMerge(
        BuildContext context,
        BaseBuildSettings settings,
        string sourceBranch,
        string targetBranch,
        string downstreamBranch,
        out bool areChangesPending )
    {
        areChangesPending = false;

        context.Console.WriteImportantMessage( $"Merging '{sourceBranch}' branch to '{targetBranch}' branch" );

        if ( !GitHelper.TryMerge( context, sourceBranch, targetBranch, "--no-commit --no-ff", true ) )
        {
            return false;
        }

        if ( !GitHelper.TryGetStatus( context, context.RepoDirectory, out var statuses )

             // We don't rely on the number of changed files, because when there are commits with the same changes,
             // it results in a merge commit with zero changed files.
             || !GitHelper.TryGetIsMergeInProgress( context, context.RepoDirectory, out var isMergeInProgress ) )
        {
            return false;
        }

        if ( isMergeInProgress )
        {
            if ( statuses.Length > 0 )
            {
                context.Console.WriteImportantMessage( "Checking the merged files for those we want to keep own." );

                // We don't merge these files downstream as they are specific to the product family version.
                var filesToKeepOwn = new HashSet<string>();

                void AddFileToKeepOwn( string path )
                {
                    var unixStylePath = path.Replace( Path.DirectorySeparatorChar, '/' );
                    filesToKeepOwn.Add( unixStylePath );
                }

                AddFileToKeepOwn( context.Product.MainVersionFilePath );
                AddFileToKeepOwn( context.Product.AutoUpdatedVersionsFilePath );
                AddFileToKeepOwn( context.Product.BumpInfoFilePath );

                Directory.EnumerateFiles( Path.Combine( context.RepoDirectory, ".teamcity" ), "*", SearchOption.AllDirectories )
                    .Select( p => Path.GetRelativePath( context.RepoDirectory, p ) )
                    .ToList()
                    .ForEach( AddFileToKeepOwn );

                foreach ( var status in statuses.Select( s => s.Split( ' ', 2, StringSplitOptions.TrimEntries ) ) )
                {
                    var fileToResolve = status[1];

                    if ( filesToKeepOwn.Contains( fileToResolve ) )
                    {
                        if ( !GitHelper.TryResolveUsingOurs( context, fileToResolve ) )
                        {
                            return false;
                        }
                    }
                }
            }

            // If not all conflicts were expected, git commit fails here.
            if ( !GitHelper.TryCommitMerge( context ) )
            {
                context.Console.WriteError( $"Merge conflicts need to be resolved manually. Merge '{sourceBranch}' branch to '{targetBranch}' branch." );
                context.Console.WriteError( $"Then create a pull request to '{downstreamBranch}' branch or execute this command again." );

                return false;
            }
        }

        // We push even if there's nothing to merge as there could be commits from manual conflict resolution.
        if ( !GitHelper.TryPush( context, settings ) )
        {
            areChangesPending = false;

            return false;
        }

        context.Console.WriteImportantMessage( $"'{sourceBranch}' branch merged to '{targetBranch}' branch." );
        areChangesPending = true;

        return true;
    }

    private static bool TryCreatePullRequest(
        BuildContext context,
        string targetBranch,
        string downstreamBranch,
        string sourceBranch,
        [NotNullWhen( true )] out string? pullRequestUrl )
    {
        context.Console.WriteImportantMessage( $"Creating pull request from '{targetBranch}' branch to '{downstreamBranch}' downstream branch" );

        if ( !GitHelper.TryGetRemoteUrl( context, out var remoteUrl ) )
        {
            pullRequestUrl = null;

            return false;
        }

        try
        {
            var pullRequestTitle = $"Downstream merge from '{sourceBranch}' branch";
            Task<string?> newPullRequestTask;

            if ( AzureDevOpsRepository.TryParse( remoteUrl, out var azureDevOpsRepository ) )
            {
                newPullRequestTask = AzureDevOpsHelper.TryCreatePullRequest(
                    context.Console,
                    azureDevOpsRepository,
                    targetBranch,
                    downstreamBranch,
                    pullRequestTitle );
            }
            else if ( GitHubRepository.TryParse( remoteUrl, out var gitHubRepository ) )
            {
                newPullRequestTask = GitHubHelper.TryCreatePullRequestAsync(
                    context.Console,
                    gitHubRepository,
                    targetBranch,
                    downstreamBranch,
                    pullRequestTitle );
            }
            else
            {
                context.Console.WriteError( $"Unknown VCS or unexpected repo URL format. Repo URL: '{remoteUrl}'." );
                pullRequestUrl = null;

                return false;
            }

            pullRequestUrl = newPullRequestTask.ConfigureAwait( false ).GetAwaiter().GetResult();

            if ( pullRequestUrl == null )
            {
                return false;
            }
        }
        catch ( Exception e )
        {
            context.Console.WriteError( e.ToString() );
            pullRequestUrl = null;

            return false;
        }

        context.Console.WriteImportantMessage( $"Pull request created. {pullRequestUrl}" );

        return true;
    }

    private static bool TryScheduleBuild(
        CiProjectConfiguration ciConfiguration,
        ConsoleHelper console,
        string targetBranch,
        string sourceBranch,
        string pullRequestUrl,
        string buildTypeId,
        [NotNullWhen( true )] out string? buildUrl )
    {
        console.WriteImportantMessage( $"Scheduling build '{buildTypeId}' of '{targetBranch}' branch" );

        if ( !TeamCityHelper.TryConnectTeamCity( ciConfiguration, console, out var tc ) )
        {
            buildUrl = null;

            return false;
        }

        var buildId = tc.ScheduleBuild(
            console,
            buildTypeId,
            $"Triggered by PostSharp.Engineering for downstream merge from '{sourceBranch}' branch to auto-complete pull request {pullRequestUrl}",
            targetBranch );

        if ( buildId == null )
        {
            buildUrl = null;

            return false;
        }

        buildUrl = $"https://postsharp.teamcity.com/viewLog.html?buildId={buildId}";
        console.WriteImportantMessage( $"Build scheduled. {buildUrl}" );

        return true;
    }

    private static void ExplainUnmergedBranches( ConsoleHelper console, IEnumerable<string> references, bool force, string? filteredBranchesDescription = null )
    {
        void Write( string message )
        {
            if ( force )
            {
                console.WriteWarning( message );
            }
            else
            {
                console.WriteError( message );
            }
        }

        Write( "There are former merge branches in the repository." );
        Write( "Before retrying, make sure that there are no important changes in these branches that need to be merged." );
        Write( "Such changes may be created when solving a merge conflict." );
        Write( "You can either finish merging of those branches or delete them." );
        Write( "If a pull request doesn't exist for these branches already, create one manually." );

        if ( force )
        {
            console.WriteWarning( "Existence of these branches is ignored because --force has been used." );
        }

        Write( "" );

        if ( filteredBranchesDescription != null )
        {
            Write( filteredBranchesDescription );
            Write( "" );
        }

        Write( "The branches are:" );

        references.Select( r => r.Replace( "refs/heads", "", StringComparison.Ordinal ) )
            .ToList()
            .ForEach( Write );
    }
}