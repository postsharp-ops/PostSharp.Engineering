// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Tools.Git;

/// <summary>
/// Handles upstream merge operations - merging code FROM an upstream (older) product version
/// INTO the current (downstream/newer) product version.
///
/// <para>
/// <b>Key Concepts:</b>
/// </para>
/// <list type="bullet">
///   <item><b>Upstream</b>: The older product version (e.g., 2026.0) from which changes flow</item>
///   <item><b>Downstream</b>: The newer product version (e.g., 2026.1) receiving the changes</item>
///   <item><b>Merge Branch</b>: A temporary branch (merge/{downstream}/{upstream}-{hash}) where the merge happens</item>
/// </list>
///
/// <para>
/// <b>Workflow:</b>
/// </para>
/// <list type="number">
///   <item>Command runs on the downstream branch (e.g., develop/2026.1)</item>
///   <item>Fetches latest from upstream branch (e.g., develop/2026.0)</item>
///   <item>Creates a merge branch from downstream</item>
///   <item>Merges upstream INTO the merge branch</item>
///   <item>Uses Claude Code to resolve any conflicts</item>
///   <item>Creates a PR from merge branch to downstream</item>
///   <item>Schedules a build on the merge branch</item>
/// </list>
///
/// <para>
/// <b>Key Difference from Old DownstreamMerge:</b>
/// The old DownstreamMerge ran on upstream and pushed TO downstream.
/// This UpstreamMerge runs on downstream and pulls FROM upstream.
/// Also, the PR is NOT auto-merged - it requires manual review.
/// </para>
/// </summary>
internal static class UpstreamMerge
{
    /// <summary>
    /// Checks if there are any pending upstream changes that haven't been merged yet.
    /// This is called before publishing to ensure all upstream changes are incorporated.
    /// </summary>
    /// <param name="context">The build context containing product and repo information.</param>
    /// <param name="settings">Build settings including the Force flag.</param>
    /// <returns>True if check passes (no pending changes or Force is used), false otherwise.</returns>
    public static bool CheckUpstreamChanges( BuildContext context, BaseBuildSettings settings )
    {
        context.Console.WriteHeading( "Checking for pending upstream changes" );
        context.Console.WriteMessage( "This check ensures all changes from upstream versions have been merged before publishing." );

        // Step 1: Check for any existing merge branches that haven't been completed
        context.Console.WriteMessage( "Step 1: Checking for incomplete merge branches..." );

        if ( !TryCheckPendingMerges( context, settings ) )
        {
            context.Console.WriteError( "Failed: Found incomplete merge branches that need attention." );

            return false;
        }

        context.Console.WriteMessage( "Step 1 complete: No blocking merge branches found." );

        // Step 2: Check for unmerged commits from upstream versions
        context.Console.WriteMessage( "Step 2: Checking for unmerged commits from upstream versions..." );

        if ( !TryCheckUnmergedCommits( context, settings ) )
        {
            context.Console.WriteError( "Failed: Found unmerged commits from upstream." );

            return false;
        }

        context.Console.WriteMessage( "Step 2 complete: No unmerged upstream commits found." );

        context.Console.WriteSuccess( settings.Force ? "Pending upstream changes check completed (some issues ignored due to --force)." : "No pending upstream changes found." );

        return true;
    }

    /// <summary>
    /// Checks each upstream product family for unmerged commits.
    /// Walks up the product family chain (e.g., 2026.1 -> 2026.0 -> 2025.1 -> ...).
    /// </summary>
    private static bool TryCheckUnmergedCommits( BuildContext context, BaseBuildSettings settings )
    {
        var upstreamProductFamily = context.Product.ProductFamily.UpstreamProductFamily;

        // Walk up the product family chain, checking each upstream version
        while ( upstreamProductFamily != null )
        {
            context.Console.WriteMessage( $"  Checking upstream version '{upstreamProductFamily.Version}' for unmerged changes..." );

            // Try to get the dependency definition for this product in the upstream family
            if ( !upstreamProductFamily.TryGetDependencyDefinition( context.Product.ProductName, out var upstreamDependencyDefinition ) )
            {
                // This product didn't exist in this upstream version - that's OK, skip it
                context.Console.WriteWarning(
                    $"  Product '{context.Product.ProductName}' doesn't exist in upstream version '{upstreamProductFamily.Version}'. " +
                    "Assuming it was introduced in a later version. Skipping." );

                break;
            }

            var upstreamBranch = upstreamDependencyDefinition.Branch;
            context.Console.WriteMessage( $"  Upstream branch: {upstreamBranch}" );

            // Fetch the upstream branch to ensure we have the latest
            context.Console.WriteMessage( $"  Fetching upstream branch '{upstreamBranch}'..." );

            if ( !GitHelper.TryFetch( context, upstreamBranch ) )
            {
                context.Console.WriteError( $"  Failed to fetch upstream branch '{upstreamBranch}'." );

                return false;
            }

            var remoteUpstreamBranch = $"remotes/origin/{upstreamBranch}";

            // Count commits in upstream that are not in current HEAD
            context.Console.WriteMessage( $"  Counting commits in '{remoteUpstreamBranch}' not present in HEAD..." );

            if ( !GitHelper.TryGetCommitsCount( context, "HEAD", remoteUpstreamBranch, upstreamProductFamily, out var commitsCount ) )
            {
                context.Console.WriteError( "  Failed to count commits." );

                return false;
            }

            context.Console.WriteMessage( $"  Found {commitsCount} unmerged commit(s) from upstream." );

            if ( commitsCount > 0 )
            {
                var message =
                    $"There are {commitsCount} unmerged changes from upstream version '{upstreamProductFamily.Version}' (branch '{upstreamBranch}').";

                if ( settings.Force )
                {
                    context.Console.WriteWarning( $"  {message} Ignoring due to --force flag." );
                }
                else
                {
                    context.Console.WriteError( $"  {message}" );
                    context.Console.WriteError( "  Run 'upstream-merge' to merge these changes, or use --force to ignore." );

                    return false;
                }
            }

            // Move to the next upstream version
            upstreamProductFamily = upstreamProductFamily.UpstreamProductFamily;
        }

        return true;
    }

    /// <summary>
    /// Checks for any incomplete merge branches in the repository.
    /// These are branches matching pattern "merge/{version}/*" that haven't been merged yet.
    /// </summary>
    private static bool TryCheckPendingMerges( BuildContext context, BaseBuildSettings settings )
    {
        var productFamily = context.Product.ProductFamily;
        var pendingBranchesExist = false;

        // Check current version and all upstream versions for pending merge branches
        while ( productFamily != null )
        {
            context.Console.WriteMessage( $"  Checking for pending merge branches for version '{productFamily.Version}'..." );

            var filter = $"merge/{productFamily.Version}/*";
            context.Console.WriteMessage( $"  Looking for branches matching pattern: {filter}" );

            if ( !GitHelper.TryGetRemoteReferences( context, settings, filter, out var references ) )
            {
                context.Console.WriteError( "  Failed to get remote references." );

                return false;
            }

            if ( references.Length > 0 )
            {
                context.Console.WriteWarning( $"  Found {references.Length} pending merge branch(es):" );

                ExplainUnmergedBranches(
                    context.Console,
                    references.Select( r => r.Reference ),
                    settings.Force );

                pendingBranchesExist = true;
            }
            else
            {
                context.Console.WriteMessage( $"  No pending merge branches found for version '{productFamily.Version}'." );
            }

            productFamily = productFamily.UpstreamProductFamily;
        }

        if ( settings.Force )
        {
            if ( pendingBranchesExist )
            {
                context.Console.WriteWarning( "  Pending merge branches exist but are being ignored due to --force flag." );
            }

            return true;
        }

        return !pendingBranchesExist;
    }

    /// <summary>
    /// Main entry point for the upstream merge operation.
    /// Merges code from the upstream development branch into the current (downstream) branch.
    ///
    /// <para>
    /// This command is designed to run on the downstream branch and pull changes from upstream.
    /// Unlike the old DownstreamMerge, the PR is NOT auto-merged and requires manual review.
    /// </para>
    /// </summary>
    /// <param name="context">The build context containing product and repo information.</param>
    /// <param name="settings">Merge settings including Force flag.</param>
    /// <returns>True if merge completed successfully, false otherwise.</returns>
    public static bool MergeUpstream( BuildContext context, UpstreamMergeSettings settings )
    {
        context.Console.WriteHeading( "Starting Upstream Merge Operation" );
        context.Console.WriteMessage( "This operation merges changes FROM an upstream (older) version INTO this (downstream) version." );
        context.Console.WriteMessage( "" );

        // ==================== STEP 1: Configure Git Credentials ====================
        context.Console.WriteMessage( "Step 1: Configuring Git credentials..." );

        // When on TeamCity, Git user credentials are set to TeamCity service account
        if ( !GitHelper.TryConfigureCredentials( context ) )
        {
            context.Console.WriteError( "Failed to configure Git credentials." );

            return false;
        }

        context.Console.WriteMessage( "Git credentials configured successfully." );

        // ==================== STEP 2: Verify Clean Repository ====================
        context.Console.WriteMessage( "" );
        context.Console.WriteMessage( "Step 2: Verifying repository is clean..." );

        if ( !GitHelper.TryGetStatus( context, context.RepoDirectory, out var statuses ) )
        {
            context.Console.WriteError( "Failed to get repository status." );

            return false;
        }

        if ( statuses.Length > 0 )
        {
            context.Console.WriteError( "The repository must be clean before running upstream merge." );
            context.Console.WriteError( "The following files have uncommitted changes:" );
            context.Console.WriteImportantMessage( string.Join( Environment.NewLine, statuses ) );

            return false;
        }

        context.Console.WriteMessage( "Repository is clean." );

        // ==================== STEP 3: Identify Product Family Versions ====================
        context.Console.WriteMessage( "" );
        context.Console.WriteMessage( "Step 3: Identifying product family versions..." );

        var product = context.Product;
        var currentProductFamily = product.ProductFamily;
        var upstreamProductFamily = currentProductFamily.UpstreamProductFamily;

        context.Console.WriteMessage( $"  Current product: {product.ProductName}" );
        context.Console.WriteMessage( $"  Current version: {currentProductFamily.Version}" );

        if ( upstreamProductFamily == null )
        {
            context.Console.WriteWarning(
                $"No upstream version configured for '{currentProductFamily.Version}'. " +
                "This is the oldest version in the family chain. Nothing to merge." );

            return true;
        }

        context.Console.WriteMessage( $"  Upstream version: {upstreamProductFamily.Version}" );

        // ==================== STEP 4: Get Branch Information ====================
        context.Console.WriteMessage( "" );
        context.Console.WriteMessage( "Step 4: Getting branch information..." );

        var currentBranch = product.DependencyDefinition.Branch;
        context.Console.WriteMessage( $"  Current (downstream) branch: {currentBranch}" );

        if ( !upstreamProductFamily.TryGetDependencyDefinition( product.ProductName, out var upstreamDependencyDefinition ) )
        {
            context.Console.WriteError( $"Product '{product.ProductName}' is not configured in upstream version '{upstreamProductFamily.Version}'." );
            context.Console.WriteError( "This product may not exist in the upstream version, or the configuration is missing." );

            return false;
        }

        var upstreamBranch = upstreamDependencyDefinition.Branch;
        context.Console.WriteMessage( $"  Upstream branch: {upstreamBranch}" );
        context.Console.WriteMessage( $"  Upstream DependencyDefinition.ProductFamily.Version: {upstreamDependencyDefinition.ProductFamily.Version}" );

        // Verify the upstream branch is different from current branch
        if ( upstreamBranch == currentBranch )
        {
            context.Console.WriteError( $"BUG: Upstream branch '{upstreamBranch}' is the same as current branch '{currentBranch}'!" );
            context.Console.WriteError( $"This indicates a configuration issue. The upstream product family ({upstreamProductFamily.Version}) " +
                                        $"returned a DependencyDefinition from family version {upstreamDependencyDefinition.ProductFamily.Version}." );
            context.Console.WriteError( "Check that the product is properly defined in each ProductFamily version." );

            return false;
        }

        // ==================== STEP 5: Verify Current Branch ====================
        context.Console.WriteMessage( "" );
        context.Console.WriteMessage( "Step 5: Verifying we're on the correct branch..." );
        context.Console.WriteMessage( $"  Expected branch: {currentBranch}" );
        context.Console.WriteMessage( $"  Actual branch: {context.Branch}" );

        if ( context.Branch != currentBranch )
        {
            context.Console.WriteError(
                $"Upstream merge must be executed on the downstream development branch ('{currentBranch}')." );
            context.Console.WriteError( $"Currently on branch '{context.Branch}'." );
            context.Console.WriteError( $"Please checkout '{currentBranch}' and try again." );

            return false;
        }

        context.Console.WriteMessage( "Branch verification passed." );

        // ==================== STEP 6: Fetch Upstream Branch ====================
        context.Console.WriteHeading( $"Merging from '{upstreamBranch}' into '{currentBranch}'" );
        context.Console.WriteMessage( "" );
        context.Console.WriteMessage( "Step 6: Fetching upstream branch to get latest changes..." );

        if ( !GitHelper.TryFetch( context, upstreamBranch ) )
        {
            context.Console.WriteError( $"Failed to fetch upstream branch '{upstreamBranch}'." );

            return false;
        }

        context.Console.WriteMessage( $"Successfully fetched '{upstreamBranch}'." );

        // ==================== STEP 7: Get Upstream Commit Hash ====================
        context.Console.WriteMessage( "" );
        context.Console.WriteMessage( "Step 7: Getting latest commit hash from upstream branch..." );

        if ( !GitHelper.TryGetCurrentCommitHash( context, $"origin/{upstreamBranch}", out var upstreamCommitHash ) )
        {
            context.Console.WriteError( "Failed to get commit hash for upstream branch." );

            return false;
        }

        if ( upstreamCommitHash == null )
        {
            context.Console.WriteError( $"Could not get commit hash for upstream branch 'origin/{upstreamBranch}'." );

            return false;
        }

        context.Console.WriteMessage( $"Upstream commit hash: {upstreamCommitHash}" );

        try
        {
            // ==================== STEP 8: Count Commits to Merge ====================
            context.Console.WriteMessage( "" );
            context.Console.WriteMessage( "Step 8: Counting commits to merge from upstream..." );

            if ( !GitHelper.TryGetCommitsCount( context, "HEAD", $"origin/{upstreamBranch}", upstreamProductFamily, out var commitsCount ) )
            {
                context.Console.WriteError( "Failed to count commits." );

                return false;
            }

            if ( commitsCount < 0 )
            {
                throw new InvalidOperationException( $"Invalid commits count: {commitsCount}" );
            }

            if ( commitsCount == 0 )
            {
                context.Console.WriteSuccess( $"No commits to merge. '{currentBranch}' is up-to-date with '{upstreamBranch}'." );

                return true;
            }

            context.Console.WriteImportantMessage( $"Found {commitsCount} commit(s) to merge from '{upstreamBranch}' into '{currentBranch}'." );

            // ==================== STEP 9: Verify PR Status Check is Configured ====================
            context.Console.WriteMessage( "" );
            context.Console.WriteMessage( "Step 9: Verifying PR status check configuration..." );

            var pullRequestStatusCheckBuildTypeId = product.DependencyDefinition.CiConfiguration.PullRequestStatusCheckBuildType;
            var isPullRequestRequired = pullRequestStatusCheckBuildTypeId != null;

            if ( !isPullRequestRequired )
            {
                context.Console.WriteError( "Upstream merge requires a pull request status check build type to be configured." );
                context.Console.WriteError( "This is needed to validate the merge before it can be approved." );

                return false;
            }

            context.Console.WriteMessage( $"PR status check build type: {pullRequestStatusCheckBuildTypeId}" );

            // ==================== STEP 10: Determine Merge Branch Name ====================
            context.Console.WriteMessage( "" );
            context.Console.WriteMessage( "Step 10: Determining merge branch name..." );

            // Merge branch name format: merge/{currentVersion}/{upstreamVersion}-{commitHash}
            // This uniquely identifies the merge based on the upstream commit being merged
            var targetBranch = $"merge/{currentProductFamily.Version}/{upstreamProductFamily.Version}-{upstreamCommitHash}";
            context.Console.WriteMessage( $"Merge branch name: {targetBranch}" );

            // ==================== STEP 11: Check for Existing Merge Branches ====================
            context.Console.WriteMessage( "" );
            context.Console.WriteMessage( "Step 11: Checking for existing merge branches..." );

            var filter = $"merge/{currentProductFamily.Version}/*";
            context.Console.WriteMessage( $"Looking for branches matching: {filter}" );

            if ( !GitHelper.TryGetRemoteReferences( context, settings, filter, out var references ) )
            {
                context.Console.WriteError( "Failed to get remote references." );

                return false;
            }

            context.Console.WriteMessage( $"Found {references.Length} existing merge branch(es)." );

            var targetBranchReference = $"refs/heads/{targetBranch}";
            var targetBranchExistsRemotely = references.Any( r => r.Reference == targetBranchReference );

            if ( targetBranchExistsRemotely )
            {
                context.Console.WriteMessage( $"Target merge branch already exists on remote: {targetBranch}" );
            }

            // ==================== STEP 12: Delete Prior Merge Branches ====================
            context.Console.WriteMessage( "" );
            context.Console.WriteMessage( "Step 12: Cleaning up prior merge branches from same upstream version..." );

            // Find merge branches from the same upstream version (but different commit hash)
            // These are obsolete and should be deleted to avoid confusion
            var formerMergeBranches = references.Where( r => r.Reference != targetBranchReference )
                .Where( r => r.Reference.StartsWith(
                    $"refs/heads/merge/{currentProductFamily.Version}/{upstreamProductFamily.Version}-",
                    StringComparison.OrdinalIgnoreCase ) )
                .ToArray();

            if ( formerMergeBranches.Length == 0 )
            {
                context.Console.WriteMessage( "No prior merge branches to clean up." );
            }
            else
            {
                context.Console.WriteMessage( $"Found {formerMergeBranches.Length} prior merge branch(es) to delete:" );

                foreach ( var formerBranch in formerMergeBranches )
                {
                    var branchName = formerBranch.Reference.Substring( "refs/heads/".Length );
                    context.Console.WriteMessage( $"  Deleting: {branchName}" );

                    if ( !GitHelper.TryDeleteRemoteBranch( context, branchName ) )
                    {
                        // Log warning but continue - branch deletion failure should not block the merge
                        context.Console.WriteWarning( $"  Failed to delete '{branchName}'. This is non-fatal, continuing..." );
                    }
                    else
                    {
                        context.Console.WriteMessage( $"  Deleted: {branchName}" );
                    }
                }
            }

            // ==================== STEP 13: Create Merge Branch (delete if exists) ====================
            context.Console.WriteMessage( "" );
            context.Console.WriteMessage( "Step 13: Creating merge branch..." );

            // If the target branch exists (remotely or locally), delete it first.
            // This ensures we always start fresh from the current downstream branch,
            // avoiding stale state where the merge branch conflicts with an updated target.
            if ( targetBranchExistsRemotely )
            {
                context.Console.WriteImportantMessage( $"Merge branch '{targetBranch}' exists on remote. Deleting to start fresh..." );

                if ( !GitHelper.TryDeleteRemoteBranch( context, targetBranch ) )
                {
                    context.Console.WriteWarning( $"Failed to delete remote branch '{targetBranch}'. Continuing anyway..." );
                }
                else
                {
                    context.Console.WriteMessage( "Remote branch deleted." );
                }
            }

            // Check if it exists locally and delete
            if ( !GitHelper.TryGetCurrentCommitHash( context, targetBranch, out var targetBranchCurrentCommitHash ) )
            {
                context.Console.WriteError( "Failed to check if branch exists locally." );

                return false;
            }

            if ( targetBranchCurrentCommitHash != null )
            {
                context.Console.WriteMessage( $"Merge branch '{targetBranch}' exists locally. Deleting..." );

                if ( !GitHelper.TryDeleteLocalBranch( context, targetBranch ) )
                {
                    context.Console.WriteWarning( $"Failed to delete local branch '{targetBranch}'. Continuing anyway..." );
                }
                else
                {
                    context.Console.WriteMessage( "Local branch deleted." );
                }
            }

            // Create fresh merge branch from current HEAD (the downstream branch)
            context.Console.WriteImportantMessage( $"Creating new merge branch: {targetBranch}" );

            if ( !GitHelper.TryCreateBranch( context, targetBranch ) )
            {
                context.Console.WriteError( "Failed to create merge branch." );

                return false;
            }

            context.Console.WriteMessage( "Successfully created merge branch." );

            // ==================== STEP 14: Push Merge Branch ====================
            context.Console.WriteMessage( "" );
            context.Console.WriteMessage( "Step 14: Pushing merge branch to remote..." );
            context.Console.WriteMessage( "This ensures the branch exists on remote before we start merging." );

            if ( !GitHelper.TryPush( context ) )
            {
                context.Console.WriteError( "Failed to push merge branch." );

                return false;
            }

            context.Console.WriteMessage( "Merge branch pushed successfully." );

            // ==================== STEP 15: Perform the Merge ====================
            context.Console.WriteMessage( "" );
            context.Console.WriteMessage( "Step 15: Performing the merge..." );

            if ( !TryMerge( context, upstreamBranch, targetBranch, currentBranch, out var areChangesPending, out var prBodyText ) )
            {
                context.Console.WriteError( "Merge operation failed." );

                return false;
            }

            if ( !areChangesPending )
            {
                // This can happen if someone manually resolved and merged the changes
                context.Console.WriteSuccess( $"No changes to merge. '{currentBranch}' is already up-to-date with '{upstreamBranch}'." );

                return true;
            }

            context.Console.WriteSuccess(
                $"Merge completed! Changes from '{upstreamBranch}' have been merged into '{targetBranch}'." );

            // ==================== STEP 16: Create Pull Request ====================
            context.Console.WriteMessage( "" );
            context.Console.WriteMessage( "Step 16: Creating pull request..." );

            if ( !TryCreatePullRequest( context, targetBranch, currentBranch, upstreamBranch, prBodyText, out var pullRequestUrl ) )
            {
                context.Console.WriteError( "Failed to create pull request." );

                return false;
            }

            // ==================== STEP 17: Schedule Build ====================
            context.Console.WriteMessage( "" );
            context.Console.WriteMessage( "Step 17: Scheduling build on merge branch..." );

            if ( !TryScheduleBuild(
                    product.DependencyDefinition.CiConfiguration,
                    context.Console,
                    targetBranch,
                    upstreamBranch,
                    pullRequestUrl,
                    pullRequestStatusCheckBuildTypeId!,
                    out var buildUrl ) )
            {
                context.Console.WriteError( "Failed to schedule build." );

                return false;
            }

            // ==================== SUCCESS ====================
            context.Console.WriteMessage( "" );
            context.Console.WriteSuccess( "========================================" );
            context.Console.WriteSuccess( "Upstream merge completed successfully!" );
            context.Console.WriteSuccess( "========================================" );
            context.Console.WriteMessage( "" );
            context.Console.WriteImportantMessage( $"Pull Request: {pullRequestUrl}" );
            context.Console.WriteImportantMessage( $"Build: {buildUrl}" );
            context.Console.WriteMessage( "" );
            context.Console.WriteWarning( "IMPORTANT: This PR requires manual review and will NOT be auto-merged." );
            context.Console.WriteMessage( "Please review the changes and merge manually when the build passes." );

            return true;
        }
        finally
        {
            // ==================== CLEANUP: Return to Original Branch ====================
            context.Console.WriteMessage( "" );
            context.Console.WriteMessage( "Cleanup: Returning to original branch..." );

            try
            {
                GitHelper.TryCheckoutAndPull( context, product.DependencyDefinition.Branch );
                context.Console.WriteMessage( $"Returned to branch '{product.DependencyDefinition.Branch}'." );
            }
            catch ( Exception e )
            {
                context.Console.WriteWarning( $"Failed to return to original branch: {e.Message}" );
                context.Console.WriteError( e.ToString() );
            }
        }
    }

    /// <summary>
    /// Performs the actual git merge operation.
    ///
    /// <para>
    /// This method:
    /// </para>
    /// <list type="number">
    ///   <item>Merges the upstream branch into the target (merge) branch using --no-commit --no-ff</item>
    ///   <item>Handles files that should keep their downstream version (Build.ps1, .teamcity/*, etc.)</item>
    ///   <item>Detects merge conflicts and invokes Claude Code to resolve them</item>
    ///   <item>Commits the merge and pushes</item>
    /// </list>
    /// </summary>
    /// <param name="context">Build context.</param>
    /// <param name="sourceBranch">The upstream branch to merge FROM.</param>
    /// <param name="targetBranch">The merge branch to merge INTO.</param>
    /// <param name="currentBranch">The downstream branch (for error messages).</param>
    /// <param name="areChangesPending">Output: true if there were changes to merge.</param>
    /// <param name="prBodyText">Output: text from Claude for the PR body (if conflicts were resolved).</param>
    /// <returns>True if merge succeeded, false otherwise.</returns>
    private static bool TryMerge(
        BuildContext context,
        string sourceBranch,
        string targetBranch,
        string currentBranch,
        out bool areChangesPending,
        out string prBodyText )
    {
        areChangesPending = false;
        prBodyText = "";

        context.Console.WriteImportantMessage( $"Merging 'origin/{sourceBranch}' into '{targetBranch}'..." );

        // Ensure credentials are configured for push operations
        if ( !GitHelper.TryConfigureCredentials( context ) )
        {
            context.Console.WriteError( "Failed to configure Git credentials." );

            return false;
        }

        // Perform the merge with --no-commit so we can handle conflicts
        // --no-ff ensures a merge commit is created even for fast-forward merges
        context.Console.WriteMessage( "Executing: git merge --no-commit --no-ff origin/{sourceBranch}" );

        if ( !GitHelper.TryMerge( context, $"origin/{sourceBranch}", targetBranch, "--no-commit --no-ff", true ) )
        {
            context.Console.WriteError( "Git merge command failed." );

            return false;
        }

        // Check the status after merge
        context.Console.WriteMessage( "Checking repository status after merge..." );

        if ( !GitHelper.TryGetStatus( context, context.RepoDirectory, out var statuses )
             || !GitHelper.TryGetIsMergeInProgress( context, context.RepoDirectory, out var isMergeInProgress ) )
        {
            context.Console.WriteError( "Failed to get repository status." );

            return false;
        }

        context.Console.WriteMessage( $"Merge in progress: {isMergeInProgress}" );
        context.Console.WriteMessage( $"Files with changes: {statuses.Length}" );

        if ( isMergeInProgress )
        {
            if ( statuses.Length > 0 )
            {
                context.Console.WriteMessage( "" );
                context.Console.WriteImportantMessage( "Processing merged files..." );

                // Track files that have unresolved conflicts
                // Claude will handle all conflict resolution, including generated files
                var filesWithConflicts = new List<string>();

                context.Console.WriteMessage( "" );
                context.Console.WriteMessage( "Processing each changed file..." );

                foreach ( var status in statuses.Select( s => s.Split( ' ', 2, StringSplitOptions.TrimEntries ) ) )
                {
                    var statusCode = status[0];
                    var fileToResolve = status[1];

                    context.Console.WriteMessage( $"  [{statusCode}] {fileToResolve}" );

                    if ( statusCode.Contains( 'U', StringComparison.Ordinal ) )
                    {
                        // Status codes containing 'U' indicate unmerged (conflicted) files:
                        // UU = both modified (conflict)
                        // AU = added by us, unmerged
                        // UA = unmerged, added by them
                        // DU = deleted by us, unmerged
                        // UD = unmerged, deleted by them
                        context.Console.WriteMessage( $"    -> CONFLICT: Requires resolution by Claude" );
                        filesWithConflicts.Add( fileToResolve );
                    }
                    else
                    {
                        context.Console.WriteMessage( $"    -> Auto-merged successfully" );
                    }
                }

                // If there are conflicts, try to resolve them using Claude
                if ( filesWithConflicts.Count > 0 )
                {
                    context.Console.WriteMessage( "" );
                    context.Console.WriteHeading( "Conflict Resolution with Claude Code" );
                    context.Console.WriteImportantMessage( $"Found {filesWithConflicts.Count} file(s) with merge conflicts:" );

                    foreach ( var file in filesWithConflicts )
                    {
                        context.Console.WriteMessage( $"  - {file}" );
                    }

                    context.Console.WriteMessage( "" );
                    context.Console.WriteMessage( "Invoking Claude Code to resolve conflicts..." );

                    if ( ClaudeCodeHelper.TryResolveMergeConflicts(
                            context.Console,
                            context.RepoDirectory,
                            sourceBranch,
                            targetBranch,
                            out prBodyText ) )
                    {
                        context.Console.WriteSuccess( "Claude successfully resolved all merge conflicts!" );
                    }
                    else
                    {
                        context.Console.WriteError( "Claude failed to resolve merge conflicts." );
                        context.Console.WriteError( "" );
                        context.Console.WriteError( "Manual resolution required:" );
                        context.Console.WriteError( $"  1. Checkout the merge branch: git checkout {targetBranch}" );
                        context.Console.WriteError( $"  2. Merge upstream manually: git merge origin/{sourceBranch}" );
                        context.Console.WriteError( "  3. Resolve conflicts in your IDE" );
                        context.Console.WriteError( "  4. Commit the merge: git commit" );
                        context.Console.WriteError( $"  5. Push: git push" );
                        context.Console.WriteError( $"  6. Create a PR to '{currentBranch}' or run this command again" );

                        return false;
                    }
                }
                else
                {
                    context.Console.WriteMessage( "" );
                    context.Console.WriteMessage( "No conflicts detected - all files auto-merged or resolved." );
                }
            }
            else
            {
                context.Console.WriteMessage( "No file changes detected (possibly identical commits)." );
            }

            // Commit the merge
            context.Console.WriteMessage( "" );
            context.Console.WriteMessage( "Committing the merge..." );

            if ( !GitHelper.TryCommitMerge( context ) )
            {
                context.Console.WriteError( "Failed to commit merge." );
                context.Console.WriteError( "" );
                context.Console.WriteError( "Manual resolution required:" );
                context.Console.WriteError( $"  1. Checkout the merge branch: git checkout {targetBranch}" );
                context.Console.WriteError( $"  2. Complete the merge manually" );
                context.Console.WriteError( $"  3. Create a PR to '{currentBranch}' or run this command again" );

                return false;
            }

            context.Console.WriteMessage( "Merge committed successfully." );
        }
        else
        {
            context.Console.WriteMessage( "No merge in progress - changes may have already been merged." );
        }

        // Push the merge commit
        context.Console.WriteMessage( "" );
        context.Console.WriteMessage( "Pushing merge commit to remote..." );

        if ( !GitHelper.TryPush( context ) )
        {
            context.Console.WriteError( "Failed to push merge commit." );
            areChangesPending = false;

            return false;
        }

        context.Console.WriteMessage( "Merge pushed successfully." );
        context.Console.WriteImportantMessage( $"'{sourceBranch}' has been merged into '{targetBranch}'." );
        areChangesPending = true;

        return true;
    }

    /// <summary>
    /// Creates a pull request from the merge branch to the downstream branch.
    /// </summary>
    /// <param name="context">Build context.</param>
    /// <param name="targetBranch">The merge branch (PR source).</param>
    /// <param name="currentBranch">The downstream branch (PR target).</param>
    /// <param name="sourceBranch">The upstream branch (for PR title).</param>
    /// <param name="prBodyText">Text from Claude for the PR body.</param>
    /// <param name="pullRequestUrl">Output: URL of the created PR.</param>
    /// <returns>True if PR was created successfully.</returns>
    private static bool TryCreatePullRequest(
        BuildContext context,
        string targetBranch,
        string currentBranch,
        string sourceBranch,
        string prBodyText,
        [NotNullWhen( true )] out string? pullRequestUrl )
    {
        context.Console.WriteImportantMessage( $"Creating pull request: {targetBranch} -> {currentBranch}" );

        // Get the remote URL to determine which VCS we're using (GitHub, Azure DevOps, etc.)
        if ( !GitHelper.TryGetRemoteUrl( context, out var remoteUrl ) )
        {
            context.Console.WriteError( "Failed to get remote URL." );
            pullRequestUrl = null;

            return false;
        }

        context.Console.WriteMessage( $"Remote URL: {remoteUrl}" );

        try
        {
            var pullRequestTitle = $"Upstream merge from '{sourceBranch}' branch";
            context.Console.WriteMessage( $"PR Title: {pullRequestTitle}" );

            if ( VcsUrlParser.TryGetRepository( remoteUrl, out var repository ) )
            {
                context.Console.WriteMessage( $"VCS Provider: {repository.Provider}" );
                context.Console.WriteMessage( "Creating pull request via API..." );

                // Note: The prBodyText from Claude is not currently used because the API
                // doesn't support setting the body. This could be extended in the future.
                var newPullRequest = repository.TryCreatePullRequestAsync(
                        context.Console,
                        targetBranch,
                        currentBranch,
                        pullRequestTitle )
                    .ConfigureAwait( false )
                    .GetAwaiter()
                    .GetResult();

                if ( !newPullRequest.Success )
                {
                    context.Console.WriteError( "Failed to create pull request via API." );
                    pullRequestUrl = null;

                    return false;
                }

                context.Console.WriteSuccess( $"Pull request created: {newPullRequest.Url}" );
                pullRequestUrl = newPullRequest.Url!;

                return true;
            }
            else
            {
                context.Console.WriteError( $"Could not parse VCS URL: '{remoteUrl}'." );
                context.Console.WriteError( "Supported formats: GitHub (git@github.com:* or https://github.com/*)" );
                pullRequestUrl = null;

                return false;
            }
        }
        catch ( Exception e )
        {
            context.Console.WriteError( $"Exception while creating pull request: {e.Message}" );
            context.Console.WriteError( e.ToString() );
            pullRequestUrl = null;

            return false;
        }
    }

    /// <summary>
    /// Schedules a TeamCity build on the merge branch.
    /// This build validates the merge before the PR can be approved.
    /// </summary>
    /// <param name="ciConfiguration">CI configuration with TeamCity settings.</param>
    /// <param name="console">Console for logging.</param>
    /// <param name="targetBranch">The merge branch to build.</param>
    /// <param name="sourceBranch">The upstream branch (for build description).</param>
    /// <param name="pullRequestUrl">URL of the PR (for build description).</param>
    /// <param name="buildTypeId">TeamCity build type ID to trigger.</param>
    /// <param name="buildUrl">Output: URL of the scheduled build.</param>
    /// <returns>True if build was scheduled successfully.</returns>
    private static bool TryScheduleBuild(
        CiProjectConfiguration ciConfiguration,
        ConsoleHelper console,
        string targetBranch,
        string sourceBranch,
        string pullRequestUrl,
        string buildTypeId,
        [NotNullWhen( true )] out string? buildUrl )
    {
        console.WriteImportantMessage( $"Scheduling build '{buildTypeId}' on branch '{targetBranch}'..." );

        // Connect to TeamCity
        console.WriteMessage( "Connecting to TeamCity..." );

        if ( !TeamCityHelper.TryConnectTeamCity( ciConfiguration, console, out var tc ) )
        {
            console.WriteError( "Failed to connect to TeamCity." );
            buildUrl = null;

            return false;
        }

        console.WriteMessage( "Connected to TeamCity." );

        // Schedule the build
        var buildComment = $"Triggered by PostSharp.Engineering for upstream merge from '{sourceBranch}' branch. Pull request: {pullRequestUrl}";
        console.WriteMessage( $"Build comment: {buildComment}" );

        var buildId = tc.ScheduleBuild(
            console,
            buildTypeId,
            buildComment,
            targetBranch );

        if ( buildId == null )
        {
            console.WriteError( "Failed to schedule build - no build ID returned." );
            buildUrl = null;

            return false;
        }

        buildUrl = $"https://postsharp.teamcity.com/viewLog.html?buildId={buildId}";
        console.WriteSuccess( $"Build scheduled: {buildUrl}" );

        return true;
    }

    /// <summary>
    /// Explains the presence of unmerged branches to the user.
    /// Provides guidance on how to handle them.
    /// </summary>
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

        Write( "" );
        Write( "========================================" );
        Write( "EXISTING MERGE BRANCHES DETECTED" );
        Write( "========================================" );
        Write( "" );
        Write( "There are existing merge branches in the repository that haven't been merged yet." );
        Write( "" );
        Write( "Before proceeding, please check if these branches contain important changes:" );
        Write( "  - They may contain manually resolved conflicts that should not be lost" );
        Write( "  - They may have a pending pull request that needs to be completed" );
        Write( "" );
        Write( "Options:" );
        Write( "  1. Complete the pending merge (create/complete the PR)" );
        Write( "  2. Delete the branch if it's no longer needed" );
        Write( "  3. Use --force to ignore and proceed anyway" );

        if ( force )
        {
            console.WriteWarning( "" );
            console.WriteWarning( "The --force flag is set, so we will proceed despite these branches." );
        }

        Write( "" );

        if ( filteredBranchesDescription != null )
        {
            Write( filteredBranchesDescription );
            Write( "" );
        }

        Write( "The affected branches are:" );

        foreach ( var reference in references )
        {
            var branchName = reference.Replace( "refs/heads/", "", StringComparison.Ordinal );
            Write( $"  - {branchName}" );
        }

        Write( "" );
    }
}
