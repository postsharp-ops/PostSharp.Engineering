// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Publishing;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Build.Helpers;

/// <summary>
/// Performs git operations for a specific <see cref="Product"/>. For pure git operations, use <see cref="GitHelper"/>.
/// </summary>
internal static class GitIntegrationHelper
{
    public static bool TryAddTagToLastCommit( BuildContext context, PublishSettings settings )
    {
        if ( !MainVersionFile.TryRead( context, out var mainVersionFileInfo ) )
        {
            return false;
        }

        if ( !ArtifactManifestFile.TryRead(
                context,
                settings.BuildConfiguration,
                out var preparedVersionInfo ) )
        {
            return false;
        }

        var versionTag = $"release/{preparedVersionInfo.PackageVersion}";

        ToolInvocationHelper.InvokeTool(
            context.Console,
            "git",
            $"ls-remote --tags origin --grep {versionTag}",
            context.RepoDirectory,
            out _,
            out var gitOutput );

        if ( gitOutput.Contains( versionTag, StringComparison.OrdinalIgnoreCase ) )
        {
            context.Console.WriteWarning( $"Repository already contains tag '{versionTag}'." );

            return true;
        }

        if ( settings.Dry )
        {
            context.Console.WriteImportantMessage( $"Dry run: Adding '{versionTag}' tag to the latest commit." );

            return true;
        }

        // Tagging the last commit with version.
        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"tag \"{versionTag}\"",
                context.RepoDirectory ) )
        {
            return false;
        }

        // Gets the remote origin.
        if ( !GitHelper.TryGetRemoteUrl( context, out var gitOrigin ) )
        {
            return false;
        }

        // Pushes tag to origin.
        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"push {gitOrigin} {versionTag}",
                context.RepoDirectory ) )
        {
            return false;
        }

        context.Console.WriteSuccess( $"Tagging the latest commit with version '{versionTag}' was successful." );

        return true;
    }

    public static bool TryCommitVersionBump( BuildContext context, Version? currentVersion, Version newVersion )
    {
        var product = context.Product;

        // Adds bumped MainVersion.props and updated BumpInfo.txt to Git staging area.
        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"add {product.MainVersionFilePath} {product.BumpInfoFilePath}",
                context.RepoDirectory ) )
        {
            return false;
        }

        // Gets the remote origin.
        if ( !GitHelper.TryGetRemoteUrl( context, out var gitOrigin ) )
        {
            return false;
        }

        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"commit -m \"<<VERSION_BUMP>> {currentVersion?.ToString() ?? "unknown"} to {newVersion}\"",
                context.RepoDirectory ) )
        {
            return false;
        }

        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"push {gitOrigin}",
                context.RepoDirectory ) )
        {
            return false;
        }

        return true;
    }

    internal static bool TryAnalyzeGitHistory(
        BuildContext context,
        MainVersionFile mainVersionFile,
        out bool hasBumpSinceLastDeployment,
        out bool hasChangesSinceLastDeployment,
        [NotNullWhen( true )] out string? lastTagVersion )
    {
        lastTagVersion = null;

        // Fetch remote for tags and commits to make sure we have the full history to compare tags against.
        if ( !GitHelper.TryFetch( context, null ) )
        {
            hasBumpSinceLastDeployment = false;
            hasChangesSinceLastDeployment = false;

            return false;
        }

        // Get string of the last published release tag matched by glob pattern and trim newline.
        var globMatch = $"release/{mainVersionFile.Release}.*";

        ToolInvocationHelper.InvokeTool(
            context.Console,
            "git",
            $"describe --abbrev=0 --tags --match \"{globMatch}\"",
            context.RepoDirectory,
            out var exitCode,
            out var gitTagOutput );

        if ( exitCode != 0 )
        {
            hasBumpSinceLastDeployment = false;
            hasChangesSinceLastDeployment = false;

            context.Console.WriteError( gitTagOutput );

            context.Console.WriteError(
                $"The repository may not have any tags matching pattern: '{globMatch}'. If so add 'release/{mainVersionFile.Release}.0{mainVersionFile.PackageVersionSuffix}' tag to initial commit." );

            return false;
        }

        var lastTag = gitTagOutput.Trim();
        lastTagVersion = lastTag.Replace( "release/", "", StringComparison.OrdinalIgnoreCase );

        // Get commits log since the last deployment formatted to one line per commit.
        // Note that the log does NOT include the released commit.
        // ReSharper disable once StringLiteralTypo
        ToolInvocationHelper.InvokeTool(
            context.Console,
            "git",
            $"log \"{lastTag}..HEAD\" --oneline",
            context.RepoDirectory,
            out exitCode,
            out var gitLogOutput );

        if ( exitCode != 0 )
        {
            hasBumpSinceLastDeployment = false;
            hasChangesSinceLastDeployment = false;

            context.Console.WriteError( gitLogOutput );

            return false;
        }

        // Check if we bumped since last deployment by looking in the Git log. 
        var gitLog = gitLogOutput.Split( ['\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );

        var versionBumpLogCommentRegex =
            new Regex( GitHelper.GetEngineeringCommitsRegex( true, false, context.Product.DependencyDefinition.ProductFamily ) );

        var lastVersionDump = gitLog.Select( ( s, i ) => (Log: s, LineNumber: i) )
            .FirstOrDefault( s => versionBumpLogCommentRegex.IsMatch( s.Log.Split( ' ', 2, StringSplitOptions.TrimEntries )[1] ) );

        hasBumpSinceLastDeployment = lastVersionDump.Log != null;

        // Get count of commits since last deployment excluding version bumps and check if there are any changes.
        if ( !GitHelper.TryGetCommitsCount( context, lastTag, "HEAD", context.Product.DependencyDefinition.ProductFamily, out var commitsSinceLastTag ) )
        {
            hasBumpSinceLastDeployment = false;
            hasChangesSinceLastDeployment = false;

            return false;
        }

        hasChangesSinceLastDeployment = commitsSinceLastTag > 0;

        return true;
    }
}