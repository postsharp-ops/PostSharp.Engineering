// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
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
/// </list>
///
/// <para>
/// <b>Workflow:</b>
/// </para>
/// <list type="number">
///   <item>Command runs on the downstream branch (e.g., develop/2026.1)</item>
///   <item>Fetches latest from upstream branch (e.g., develop/2026.0)</item>
///   <item>Merges upstream INTO the downstream branch directly</item>
///   <item>Uses Claude Code to resolve any conflicts</item>
///   <item>If Claude resolves the conflicts, commits and pushes the merge to the downstream branch</item>
///   <item>If Claude cannot resolve the conflicts, leaves the in-progress merge in the working tree and fails</item>
/// </list>
///
/// <para>
/// <b>No merge branches:</b>
/// This operation no longer creates intermediate <c>merge/{version}/...</c> branches or pull requests.
/// If the AI can perform the merge, the result goes straight into the downstream branch; if it cannot,
/// the operation fails so a human can finish the merge locally. Deployment is handled separately by the
/// standard deploy procedure (version bump + deploy), not by this command.
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

        // Check for unmerged commits from upstream versions
        context.Console.WriteMessage( "Checking for unmerged commits from upstream versions..." );

        if ( !TryCheckUnmergedCommits( context, settings ) )
        {
            context.Console.WriteError( "Failed: Found unmerged commits from upstream." );

            return false;
        }

        context.Console.WriteMessage( "No unmerged upstream commits found." );

        context.Console.WriteSuccess(
            settings.Force ? "Pending upstream changes check completed (some issues ignored due to --force)." : "No pending upstream changes found." );

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
    /// Main entry point for the upstream merge operation.
    /// Merges code from the upstream development branch directly into the current (downstream) branch.
    ///
    /// <para>
    /// This command is designed to run on the downstream branch and pull changes from upstream.
    /// If the AI can resolve the merge, the result is pushed straight to the downstream branch; otherwise
    /// the in-progress merge is left in the working tree and the operation fails. No merge branch or pull
    /// request is created.
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

        // ==================== STEP 2: Force Clean Repository ====================
        context.Console.WriteMessage( "" );
        context.Console.WriteMessage( "Step 2: Force cleaning repository..." );

        context.Console.WriteMessage( "Running git reset --hard..." );

        if ( !GitHelper.TryResetHard( context ) )
        {
            context.Console.WriteError( "Failed to reset repository." );

            return false;
        }

        context.Console.WriteMessage( "Running git clean -xfd..." );

        if ( !GitHelper.TryClean( context ) )
        {
            context.Console.WriteError( "Failed to clean repository." );

            return false;
        }

        context.Console.WriteMessage( "Repository is now clean." );

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

            context.Console.WriteError(
                $"This indicates a configuration issue. The upstream product family ({upstreamProductFamily.Version}) " +
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
            context.Console.WriteError( $"Upstream merge must be executed on the downstream development branch ('{currentBranch}')." );
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

        // ==================== STEP 7: Count Commits to Merge ====================
        context.Console.WriteMessage( "" );
        context.Console.WriteMessage( "Step 7: Counting commits to merge from upstream..." );

        if ( !GitHelper.TryGetCommitsCount( context, "HEAD", $"origin/{upstreamBranch}", upstreamProductFamily, out var commitsCount ) )
        {
            context.Console.WriteError( "Failed to count commits." );

            return false;
        }

        if ( commitsCount < 0 )
        {
            context.Console.WriteError( $"Invalid commits count: {commitsCount}" );

            return false;
        }

        if ( commitsCount == 0 )
        {
            context.Console.WriteSuccess( $"No commits to merge. '{currentBranch}' is up-to-date with '{upstreamBranch}'." );

            return true;
        }

        context.Console.WriteImportantMessage( $"Found {commitsCount} commit(s) to merge from '{upstreamBranch}' into '{currentBranch}'." );

        // ==================== STEP 8: Perform the Merge ====================
        context.Console.WriteMessage( "" );
        context.Console.WriteMessage( "Step 8: Performing the merge directly into the downstream branch..." );

        if ( !TryMerge( context, upstreamBranch, currentBranch, out var areChangesPending ) )
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

        // ==================== SUCCESS ====================
        context.Console.WriteMessage( "" );
        context.Console.WriteSuccess( "========================================" );
        context.Console.WriteSuccess( "Upstream merge completed successfully!" );
        context.Console.WriteSuccess( "========================================" );
        context.Console.WriteMessage( "" );
        context.Console.WriteImportantMessage( $"Changes from '{upstreamBranch}' have been merged into '{currentBranch}' and pushed." );
        context.Console.WriteMessage( "Deployment is handled separately by the standard deploy procedure (version bump + deploy)." );

        return true;
    }

    /// <summary>
    /// Performs the actual git merge operation directly into the downstream branch.
    ///
    /// <para>
    /// This method:
    /// </para>
    /// <list type="number">
    ///   <item>Merges the upstream branch into the (currently checked out) downstream branch using --no-commit --no-ff</item>
    ///   <item>Detects merge conflicts and invokes Claude Code to resolve them</item>
    ///   <item>If Claude resolves them, regenerates scripts, commits the merge and pushes</item>
    ///   <item>If Claude cannot resolve them, leaves the in-progress merge in the working tree and returns false</item>
    /// </list>
    /// </summary>
    /// <param name="context">Build context.</param>
    /// <param name="sourceBranch">The upstream branch to merge FROM.</param>
    /// <param name="downstreamBranch">The downstream branch to merge INTO (must be the current branch).</param>
    /// <param name="areChangesPending">Output: true if there were changes to merge and push.</param>
    /// <returns>True if merge succeeded, false otherwise.</returns>
    private static bool TryMerge(
        BuildContext context,
        string sourceBranch,
        string downstreamBranch,
        out bool areChangesPending )
    {
        areChangesPending = false;

        context.Console.WriteImportantMessage( $"Merging 'origin/{sourceBranch}' into '{downstreamBranch}'..." );

        // Ensure credentials are configured for push operations
        if ( !GitHelper.TryConfigureCredentials( context ) )
        {
            context.Console.WriteError( "Failed to configure Git credentials." );

            return false;
        }

        // Perform the merge with --no-commit so we can handle conflicts
        // --no-ff ensures a merge commit is created even for fast-forward merges
        context.Console.WriteMessage( $"Executing: git merge --no-commit --no-ff origin/{sourceBranch}" );

        if ( !GitHelper.TryMerge( context, $"origin/{sourceBranch}", downstreamBranch, "--no-commit --no-ff", true ) )
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
                        context.Console.WriteMessage( "    -> CONFLICT: Requires resolution by Claude" );
                        filesWithConflicts.Add( fileToResolve );
                    }
                    else
                    {
                        context.Console.WriteMessage( "    -> Auto-merged successfully" );
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
                            downstreamBranch,
                            out _ ) )
                    {
                        context.Console.WriteSuccess( "Claude successfully resolved all merge conflicts!" );

                        // Regenerate scripts after conflict resolution to ensure generated files are up-to-date
                        context.Console.WriteMessage( "" );
                        context.Console.WriteMessage( "Regenerating scripts after conflict resolution..." );

                        if ( !ToolInvocationHelper.InvokePowershell(
                                context.Console,
                                "Build.ps1",
                                "generate-scripts",
                                context.RepoDirectory ) )
                        {
                            context.Console.WriteError( "Failed to regenerate scripts." );

                            return false;
                        }

                        context.Console.WriteMessage( "Scripts regenerated successfully." );
                    }
                    else
                    {
                        // Per design, we never create a merge branch. If the AI cannot resolve the
                        // conflicts, we leave the in-progress merge in the working tree (we do NOT
                        // abort or reset) so a human can finish it locally, and we fail the operation.
                        context.Console.WriteError( "Claude failed to resolve merge conflicts." );
                        context.Console.WriteError( "" );
                        context.Console.WriteError( "The in-progress merge has been left in the working tree. Manual resolution required:" );
                        context.Console.WriteError( $"  1. Resolve the conflicts in '{downstreamBranch}' in your IDE" );
                        context.Console.WriteError( "  2. Stage the result: git add -A" );
                        context.Console.WriteError( "  3. Regenerate scripts: ./Build.ps1 generate-scripts" );
                        context.Console.WriteError( "  4. Commit the merge: git commit --no-edit" );
                        context.Console.WriteError( "  5. Push: git push" );

                        return false;
                    }
                }
                else
                {
                    context.Console.WriteMessage( "" );
                    context.Console.WriteMessage( "No conflicts detected - all files auto-merged or resolved." );
                }

                // Stage the regenerated files
                if ( !ToolInvocationHelper.InvokeTool(
                        context.Console,
                        "git",
                        "add -A",
                        context.RepoDirectory ) )
                {
                    context.Console.WriteError( "Failed to stage regenerated scripts." );

                    return false;
                }

                context.Console.WriteMessage( "Regenerated scripts staged." );
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
                // Leave the in-progress merge in the working tree for manual completion.
                context.Console.WriteError( "Failed to commit merge." );
                context.Console.WriteError( "" );
                context.Console.WriteError( "The in-progress merge has been left in the working tree. Complete it manually and push." );

                return false;
            }

            context.Console.WriteMessage( "Merge committed successfully." );
        }
        else
        {
            context.Console.WriteMessage( "No merge in progress - changes may have already been merged." );
        }

        // Push the merge commit directly to the downstream branch
        context.Console.WriteMessage( "" );
        context.Console.WriteMessage( $"Pushing merge commit to '{downstreamBranch}'..." );

        if ( !GitHelper.TryPush( context ) )
        {
            context.Console.WriteError( "Failed to push merge commit." );
            areChangesPending = false;

            return false;
        }

        context.Console.WriteMessage( "Merge pushed successfully." );
        context.Console.WriteImportantMessage( $"'{sourceBranch}' has been merged into '{downstreamBranch}'." );
        areChangesPending = true;

        return true;
    }
}
