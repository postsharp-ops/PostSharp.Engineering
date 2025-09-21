// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Utilities;

[PublicAPI]
public static class GitHelper
{
    private static bool TryAddOrigin( ConsoleHelper console, string repoDirectory, string branch )
    {
        // Add origin/<branch> branch to the list of currently tracked branches because local repository may be initialized with only the default branch.
        if ( !ToolInvocationHelper.InvokeTool(
                console,
                "git",
                $"remote set-branches --add origin {branch}",
                repoDirectory ) )
        {
            return false;
        }

        return true;
    }

    public static bool TryFetch( BuildContext context, string? branch ) => TryFetch( context.Console, context.RepoDirectory, branch );

    public static bool TryFetch( ConsoleHelper console, string repoDirectory, string? branch )
    {
        if ( branch != null && !TryAddOrigin( console, repoDirectory, branch ) )
        {
            return false;
        }

        if ( !ToolInvocationHelper.InvokeTool(
                console,
                "git",
                $"fetch",
                repoDirectory ) )
        {
            return false;
        }

        return true;
    }

    public static bool TryCheckoutAndPull( BuildContext context, string branch )
    {
        if ( !TryFetch( context, branch ) )
        {
            return false;
        }

        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"checkout {branch}",
                context.RepoDirectory ) )
        {
            return false;
        }

        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"pull origin {branch}",
                context.RepoDirectory ) )
        {
            return false;
        }

        return true;
    }

    public static bool TryPullAndMergeAndPush( BuildContext context, BuildSettings settings, string targetBranch )
    {
        // We don't use context.Branch here in case the current branch has changed.
        if ( !TryGetCurrentBranch( context, out var sourceBranch ) )
        {
            return false;
        }

        context.Console.WriteMessage( $"Merging branch '{sourceBranch}' to '{targetBranch}'." );

        // Checkout to target branch branch and pull to update the local repository.
        if ( !TryCheckoutAndPull( context, targetBranch ) )
        {
            return false;
        }

        // Attempts merging from the source branch, forcing conflicting hunks to be auto-resolved in favour of the branch being merged.
        if ( !TryMerge( context, sourceBranch, targetBranch, "--strategy-option theirs" ) )
        {
            return false;
        }

        // Push the target branch.
        if ( !TryPush( context, settings ) )
        {
            return false;
        }

        context.Console.WriteMessage( $"Merging '{sourceBranch}' branch into '{targetBranch}' branch was successful." );

        return true;
    }

    public static bool TryCreateBranch( BuildContext context, string branch )
    {
        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"checkout -b {branch}",
                context.RepoDirectory ) )
        {
            return false;
        }

        if ( !TryAddOrigin( context.Console, context.RepoDirectory, branch ) )
        {
            return false;
        }

        return true;
    }

    public static bool TryGetCurrentBranch( BuildContext context, [NotNullWhen( true )] out string? currentBranch )
        => TryGetCurrentBranch( context.Console, context.RepoDirectory, out currentBranch );

    public static bool TryGetCurrentBranch( ConsoleHelper console, string repoDirectory, [NotNullWhen( true )] out string? currentBranch )
    {
        ToolInvocationHelper.InvokeTool(
            console,
            "git",
            $"branch --show-current",
            repoDirectory,
            out var gitExitCode,
            out var gitOutput,
            ToolInvocationOptions.Default with { Silent = true } );

        if ( gitExitCode != 0 )
        {
            console.WriteError( gitOutput );
            currentBranch = null;

            return false;
        }

        currentBranch = gitOutput.Trim();

        return true;
    }

    public static bool TryGetCurrentCommitHash( BuildContext context, [NotNullWhen( true )] out string? currentCommitHash )
        => TryGetCurrentCommitHash( context.Console, context.RepoDirectory, out currentCommitHash );

    public static bool TryGetCurrentCommitHash( ConsoleHelper console, string repoDirectory, [NotNullWhen( true )] out string? currentCommitHash )
    {
        if ( !TryGetCurrentCommitHash( console, repoDirectory, "HEAD", out currentCommitHash ) )
        {
            return false;
        }

        if ( currentCommitHash == null )
        {
            console.WriteError( "Failed to get current commit hash." );

            return false;
        }

        return true;
    }

    public static bool TryGetCurrentCommitHash( BuildContext context, string reference, out string? currentCommitHash )
        => TryGetCurrentCommitHash( context.Console, context.RepoDirectory, reference, out currentCommitHash );

    public static bool TryGetCurrentCommitHash( ConsoleHelper console, string repoDirectory, string reference, out string? currentCommitHash )
    {
        ToolInvocationHelper.InvokeTool(
            console,
            "git",
            $"rev-parse --verify --quiet {reference}",
            repoDirectory,
            out var gitExitCode,
            out var gitOutput );

        if ( gitExitCode != 0 )
        {
            currentCommitHash = null;

            // If the reference doesn't exist, the command returns non-zero exit code and no output.
            if ( !string.IsNullOrEmpty( gitOutput ) )
            {
                console.WriteError( gitOutput );

                return false;
            }
        }
        else
        {
            currentCommitHash = gitOutput.Trim();
        }

        return true;
    }

    public static bool TryGetCommitsCount( BuildContext context, string from, string to, out int count, string options = "" )
    {
        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"rev-list --count \"{from}..{to}\" {options}",
                context.RepoDirectory,
                out _,
                out var output ) )
        {
            context.Console.WriteError( output );
            count = -1;

            return false;
        }

        count = int.Parse( output, CultureInfo.InvariantCulture );

        return true;
    }

    public static string GetEngineeringCommitsRegex( bool includeVersionBump, bool includeDependenciesUpdate, ProductFamily? family )
    {
        var regex = "^";

        if ( includeDependenciesUpdate )
        {
            regex += "<<DEPENDENCIES_UPDATED>>";

            if ( includeVersionBump )
            {
                regex += "|";
            }
        }

        if ( includeVersionBump )
        {
            if ( family == null )
            {
                throw new ArgumentNullException( nameof(family), "Product family is required to create version bump regex." );
            }

            var fromGroupName = includeDependenciesUpdate ? ":" : "<from>";
            var toGroupName = includeDependenciesUpdate ? ":" : "<to>";
            var familyVersionRegex = family.Version.Replace( ".", @"\.", StringComparison.Ordinal );

            regex += $@"<<VERSION_BUMP>> (?{fromGroupName}unknown|{familyVersionRegex}\.\d+) to (?{toGroupName}{familyVersionRegex}\.\d+)";
        }

        regex += "$";

        return regex;
    }

    public static bool TryGetCommitsCount( BuildContext context, string from, string to, ProductFamily sourceFamily, out int count )
    {
        // This is to consider only version bumps from the source family release. (E.g. 2023.1)
        // Downstream merge would otherwise break the logic and version bump would be skipped.
        var regex = GetEngineeringCommitsRegex( true, true, sourceFamily );
        var versionBumpLogCommentRegex = new Regex( regex );

        // The --perl-regexp makes the --grep work with C# regexes. There are some differences though, so always test all cases.
        return TryGetCommitsCount(
            context,
            from,
            to,
            out count,
            $"--invert-grep --perl-regexp --grep=\"{versionBumpLogCommentRegex}\"" );
    }

    public static bool TryGetRemoteReferences(
        BuildContext context,
        BaseBuildSettings settings,
        string filter,
        [NotNullWhen( true )] out (string CommitId, string Reference)[]? references )
    {
        references = null;

        if ( !TryGetOriginUrl( context, settings, out var originUrl ) )
        {
            return false;
        }

        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"ls-remote {originUrl} {filter}",
                context.RepoDirectory,
                out _,
                out var output ) )
        {
            context.Console.WriteError( output );

            return false;
        }

        // This command doesn't have a porcelain switch and git can include warnings in the output,
        // so we filter out lines that don't represent a reference.

        // Example of an output of this command:
        // git: 'credential-manager' is not a git command. See 'git --help'.
        //
        // The most similar command is
        //    credential-manager-core
        // ef0e24989cea502b873ec2b8db308eb57e014e47        refs/heads/merge/2023.2/2023.1-e23e936ad5de5d979187dc90cd352a69275fb2d7

        var lsRegex = new Regex( @"^(?<commit>[^\s]+)[\s]+(?<ref>[^\s]+)$" );

        references = output.Split( "\n", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries )
            .Select( l => lsRegex.Match( l ) )
            .Where( m => m.Success )
            .Select( m => (m.Groups["commit"].Value, m.Groups["ref"].Value) )
            .ToArray();

        return true;
    }

    public static bool TryCommitAll( BuildContext context, string message )
    {
        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"commit -am \"{message}\"",
                context.RepoDirectory ) )
        {
            return false;
        }

        return true;
    }

    public static bool TryCommitMerge( BuildContext context )
    {
        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                "commit --no-edit",
                context.RepoDirectory ) )
        {
            return false;
        }

        return true;
    }

    public static bool TryMerge( BuildContext context, string sourceBranch, string targetBranch, string options = "", bool ignoreConflicts = false )
    {
        // Check that the current branch is the target branch.
        if ( !TryGetCurrentBranch( context, out var currentBranch ) )
        {
            return false;
        }

        if ( currentBranch != targetBranch )
        {
            context.Console.WriteError( $"The current branch is '{currentBranch}', but should be '{targetBranch}'." );

            return false;
        }

        var command = "git";
        var arguments = $"merge {sourceBranch} {options}";

        if ( ignoreConflicts )
        {
            var success = ToolInvocationHelper.InvokeTool(
                context.Console,
                command,
                arguments,
                context.RepoDirectory,
                out _,
                out var output );

            context.Console.WriteMessage( output );

            if ( success )
            {
                return true;
            }
            else if ( output.Split( '\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries )
                         .LastOrDefault()
                         ?.Equals( "Automatic merge failed; fix conflicts and then commit the result.", StringComparison.Ordinal ) ?? false )
            {
                // Git merge always returns the same error code. 
                return true;
            }
            else
            {
                return false;
            }
        }
        else if ( !ToolInvocationHelper.InvokeTool(
                     context.Console,
                     command,
                     arguments,
                     context.RepoDirectory ) )
        {
            return false;
        }

        return true;
    }

    // https://stackoverflow.com/a/48117629/4100001
    public static bool TryResolveUsingOurs( BuildContext context, string file )
    {
        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"checkout HEAD -- {file}",
                context.RepoDirectory ) )
        {
            return false;
        }

        return true;
    }

    private static bool TryGetOriginUrl( BuildContext context, BaseBuildSettings settings, [NotNullWhen( true )] out string? url )
    {
        url = null;

        ToolInvocationHelper.InvokeTool(
            context.Console,
            "git",
            $"remote get-url origin",
            context.RepoDirectory,
            out var gitExitCode,
            out var gitOutput );

        if ( gitExitCode != 0 )
        {
            context.Console.WriteError( gitOutput );

            return false;
        }

        url = gitOutput.Trim();

        var isHttps = url.StartsWith( "https", StringComparison.InvariantCulture );

        // When on TeamCity, origin will be updated to form including Git authentication credentials.
        if ( isHttps && TeamCityHelper.IsTeamCityBuild( settings ) )
        {
            if ( !TeamCityHelper.TryGetTeamCitySourceWriteToken(
                    out var teamcitySourceWriteTokenEnvironmentVariableName,
                    out var teamcitySourceCodeWritingToken ) )
            {
                context.Console.WriteImportantMessage(
                    $"{teamcitySourceWriteTokenEnvironmentVariableName} environment variable is not set. Using default credentials." );
            }
            else
            {
                url = url.Insert( 8, $"teamcity%40postsharp.net:{teamcitySourceCodeWritingToken}@" );
            }
        }

        return true;
    }

    public static bool TryPush( BuildContext context, BaseBuildSettings settings )
    {
        if ( !TryGetOriginUrl( context, settings, out var originUrl ) )
        {
            return false;
        }

        // Push completed merge operation to remote.
        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"push {originUrl}",
                context.RepoDirectory ) )
        {
            return false;
        }

        return true;
    }

    public static bool TryGetStatus( BuildContext context, string repoDirectory, [NotNullWhen( true )] out string[]? status )
    {
        if ( !ToolInvocationHelper.InvokeTool(
                 context.Console,
                 "git",
                 "status --porcelain",
                 repoDirectory,
                 out var exitCode,
                 out var statusOutput )
             || exitCode != 0 )
        {
            context.Console.WriteError( statusOutput );
            status = null;

            return false;
        }

        // Environment.NewLine is not correct here.
        status = statusOutput.Split( '\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );

        return true;
    }

    public static bool TryGetIsMergeInProgress( BuildContext context, string repo, out bool isMergeInProgress )
    {
        if ( !ToolInvocationHelper.InvokeTool(
                 context.Console,
                 "git",
                 "rev-parse -q --verify MERGE_HEAD",
                 repo,
                 out var exitCode,
                 out var output )
             || exitCode != 0 )
        {
            isMergeInProgress = false;

            // Exit code 1 with no output means that a merge is not in progress. Otherwise, something unexpected has happened.
            if ( exitCode == 1 && output == "" )
            {
                return true;
            }

            context.Console.WriteError( output );

            return false;
        }

        // Exit code 0 means that merge is in progress.
        isMergeInProgress = true;

        return true;
    }

    public static bool CheckNoChange( BuildContext context, CommonCommandSettings settings, string repo )
    {
        if ( !settings.Force )
        {
            if ( !TryGetStatus( context, repo, out var status ) )
            {
                return false;
            }

            if ( status.Length > 0 )
            {
                context.Console.WriteError( $"There are non-committed changes in '{repo}' Use --force." );
                context.Console.WriteImportantMessage( string.Join( Environment.NewLine, status ) );

                return false;
            }
        }

        return true;
    }

    public static bool TryGetRemoteUrl( BuildContext context, [NotNullWhen( true )] out string? url ) => TryGetRemoteUrl( context, "origin", out url );

    public static bool TryGetRemoteUrl( BuildContext context, string remoteName, [NotNullWhen( true )] out string? url )
    {
        if ( !ToolInvocationHelper.InvokeTool(
                 context.Console,
                 "git",
                 $"config --get remote.{remoteName}.url",
                 context.RepoDirectory,
                 out var exitCode,
                 out var output )
             || exitCode != 0 )
        {
            context.Console.WriteError( output );
            url = null;

            return false;
        }

        url = output.Trim();

        return true;
    }

    public static bool TryGetLatestCommitDate( BuildContext context, [NotNullWhen( true )] out string? buildDate )
    {
        // We intentionally ignore any commit that is not by postsharp.net, so customers can add their own commit without
        // affecting the build date restriction of their support subscription.
        if ( !ToolInvocationHelper.InvokeTool(
                 context.Console,
                 "git",
                 $"log -1 --format=%cd --date=iso-strict --author=@postsharp.net",
                 context.RepoDirectory,
                 out var exitCode,
                 out var output )
             || exitCode != 0 )
        {
            context.Console.WriteError( output );
            buildDate = null;

            return false;
        }

        buildDate = output.Trim();

        if ( string.IsNullOrEmpty( buildDate ) )
        {
            context.Console.WriteError( $"Cannot find any commits from *@postsharp.net on this branch." );

            return false;
        }

        return true;
    }

    public static bool ConfigureAuthentication( BuildContext context )
    {
        var console = context.Console;
        var environmentVariable = context.Product.DependencyDefinition.VcsRepository.TokenEnvironmentVariableName;
        
        console.WriteMessage( "Configuring git credentials." );

        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            var tempFileName = Path.Combine( Path.GetTempPath(), "git-askpass.cmd" );
            File.WriteAllText( tempFileName, $"@echo off\r\necho %{environmentVariable}%" );

            if ( !ToolInvocationHelper.InvokeTool( console, "git", "config --global credential.helper \"\"" ) )
            {
                return false;
            }

            if ( !ToolInvocationHelper.InvokeTool( console, "git", $"config --global core.askPass \"{tempFileName}\"" ) )
            {
                return false;
            }
        }
        else
        {
            throw new PlatformNotSupportedException();
        }

        return true;
    }
}