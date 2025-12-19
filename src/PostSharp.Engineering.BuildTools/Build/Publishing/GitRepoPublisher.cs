// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using System.IO.Compression;

namespace PostSharp.Engineering.BuildTools.Build.Publishing;

/// <summary>
/// Publishes a zip file to a git repository by cloning the repo, replacing its content with the zip file content,
/// then committing and pushing.
/// </summary>
[PublicAPI]
public class GitRepoPublisher : ArtifactPublisher
{
    private readonly ParametricString _gitHubUrl;
    private readonly ParametricString _commitMessage;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitRepoPublisher"/> class.
    /// </summary>
    /// <param name="files">The pattern matching the zip file(s) to publish.</param>
    /// <param name="gitHubUrl">The GitHub repository URL to publish to. Supports parameters like <c>$(PackageVersion)</c>.</param>
    /// <param name="commitMessage">The commit message to use when pushing changes. Supports parameters like <c>$(PackageVersion)</c>.</param>
    public GitRepoPublisher( Pattern files, ParametricString gitHubUrl, ParametricString commitMessage ) : base( files )
    {
        this._gitHubUrl = gitHubUrl;
        this._commitMessage = commitMessage;
    }

    public override SuccessCode PublishFile(
        BuildContext context,
        PublishSettings settings,
        string file,
        BuildArguments buildArguments,
        BuildConfigurationInfo configuration )
    {
        var console = context.Console;

        console.WriteMessage( $"Publishing '{file}' to git repository '{this._gitHubUrl}'." );

        // Expand parametric strings like $(PackageVersion).
        var gitHubUrl = this._gitHubUrl.ToString( buildArguments );
        var commitMessage = this._commitMessage.ToString( buildArguments );

        if ( string.IsNullOrEmpty( gitHubUrl ) )
        {
            console.WriteError( "The GitHub URL is empty." );

            return SuccessCode.Fatal;
        }

        // Create a temporary directory for cloning.
        var tempDirectory = Path.Combine( Path.GetTempPath(), $"GitRepoPublisher_{Guid.NewGuid():N}" );

        try
        {
            Directory.CreateDirectory( tempDirectory );

            // Clone the repository (shallow clone of default branch).
            console.WriteImportantMessage( $"Cloning repository '{gitHubUrl}' to '{tempDirectory}'." );

            if ( !ToolInvocationHelper.InvokeTool(
                    console,
                    "git",
                    $"clone --depth 1 \"{gitHubUrl}\" .",
                    tempDirectory ) )
            {
                console.WriteError( "Failed to clone the repository." );

                return SuccessCode.Error;
            }

            // Delete all files in the repo except .git directory.
            console.WriteMessage( "Removing existing content from the repository." );

            foreach ( var entry in Directory.EnumerateFileSystemEntries( tempDirectory ) )
            {
                var name = Path.GetFileName( entry );

                if ( name.Equals( ".git", StringComparison.OrdinalIgnoreCase ) )
                {
                    continue;
                }

                if ( Directory.Exists( entry ) )
                {
                    Directory.Delete( entry, true );
                }
                else
                {
                    File.Delete( entry );
                }
            }

            // Extract the zip file content to the repo directory.
            console.WriteImportantMessage( $"Extracting '{file}' to repository." );

            ZipFile.ExtractToDirectory( file, tempDirectory, true );

            // Check if there are any changes.
            if ( !ToolInvocationHelper.InvokeTool(
                    console,
                    "git",
                    "status --porcelain",
                    tempDirectory,
                    out _,
                    out var statusOutput ) )
            {
                console.WriteError( "Failed to get git status." );

                return SuccessCode.Error;
            }

            var changes = statusOutput.Split( '\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );

            if ( changes.Length == 0 )
            {
                console.WriteSuccess( "No changes to commit. Repository is up to date." );

                return SuccessCode.Success;
            }

            console.WriteMessage( $"Found {changes.Length} changes to commit." );

            // Stage all changes.
            if ( !ToolInvocationHelper.InvokeTool(
                    console,
                    "git",
                    "add -A",
                    tempDirectory ) )
            {
                console.WriteError( "Failed to stage changes." );

                return SuccessCode.Error;
            }

            // Commit the changes.
            // Escape double quotes in the commit message.
            var escapedMessage = commitMessage.Replace( "\"", "\\\"", StringComparison.Ordinal );

            if ( !ToolInvocationHelper.InvokeTool(
                    console,
                    "git",
                    $"commit -m \"{escapedMessage}\"",
                    tempDirectory ) )
            {
                console.WriteError( "Failed to commit changes." );

                return SuccessCode.Error;
            }

            // Push to remote (skip in dry mode).
            if ( settings.Dry )
            {
                console.WriteImportantMessage( "Dry run: Skipping push to remote repository." );
                console.WriteSuccess( $"Dry run completed successfully for '{file}' to '{gitHubUrl}'." );
            }
            else
            {
                console.WriteImportantMessage( "Pushing changes to remote repository." );

                if ( !ToolInvocationHelper.InvokeTool(
                        console,
                        "git",
                        "push",
                        tempDirectory ) )
                {
                    console.WriteError( "Failed to push changes to remote repository." );

                    return SuccessCode.Error;
                }

                console.WriteSuccess( $"Successfully published '{file}' to '{gitHubUrl}'." );
            }

            return SuccessCode.Success;
        }
        catch ( Exception e )
        {
            console.WriteError( $"Error publishing to git repository: {e.Message}" );

            return SuccessCode.Error;
        }
        finally
        {
            // Clean up the temporary directory.
            try
            {
                if ( Directory.Exists( tempDirectory ) )
                {
                    // Reset read-only attributes on .git files before deleting.
                    foreach ( var filePath in Directory.EnumerateFiles( tempDirectory, "*", SearchOption.AllDirectories ) )
                    {
                        File.SetAttributes( filePath, FileAttributes.Normal );
                    }

                    Directory.Delete( tempDirectory, true );
                }
            }
            catch ( Exception e )
            {
                console.WriteWarning( $"Failed to clean up temporary directory '{tempDirectory}': {e.Message}" );
            }
        }
    }
}