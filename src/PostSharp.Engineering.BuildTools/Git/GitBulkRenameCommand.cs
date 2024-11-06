// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Utilities;
using Spectre.Console.Cli;
using System;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Git
{
    /// <summary>
    /// Renames files in bulk using <c>git rename</c>.
    /// </summary>
    [UsedImplicitly]
    internal class GitBulkRenameCommand : Command<GitBulkRenameSettings>
    {
        // ReSharper disable RedundantNullableFlowAttribute
        public override int Execute( [System.Diagnostics.CodeAnalysis.NotNull] CommandContext context, [System.Diagnostics.CodeAnalysis.NotNull] GitBulkRenameSettings settings )
        {
            // https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/file-system/how-to-iterate-through-a-directory-tree

            var console = new ConsoleHelper();

            var root = new DirectoryInfo( settings.RepositoryRoot );

            if ( root.GetDirectories( ".git" ).Length == 0 )
            {
                console.WriteError( $"'{settings.RepositoryRoot}' is not a repository root." );

                return 1;
            }

            var replacement = settings.NewSubstring;

            if ( replacement == "EMPTY" )
            {
                replacement = "";
            }

            if ( !RenameAll( console, root, settings.OriginalSubstring, replacement, root.FullName ) )
            {
                return 1;
            }

            return 0;
        }

        private static bool RenameAll( ConsoleHelper console, DirectoryInfo directory, string originalSubstring, string newSubstring, string rootPath )
        {
            if ( directory.Name == ".git" )
            {
                return true;
            }

            var success = true;
            var subdirectories = directory.GetDirectories();
            var isEmpty = true;

            foreach ( var subdirectory in subdirectories )
            {
                isEmpty = false;
                success &= RenameAll( console, subdirectory, originalSubstring, newSubstring, rootPath );
            }

            foreach ( var file in directory.GetFiles() )
            {
                isEmpty = false;
                success &= GitRename( console, file, directory, originalSubstring, newSubstring, rootPath );
            }

            if ( !isEmpty )
            {
                success &= GitRename( console, directory, directory.Parent!, originalSubstring, newSubstring, rootPath );
            }
            else
            {
                console.WriteWarning( $"Cannot rename '{directory.FullName}' because it is an empty directory." );
            }

            return success;
        }

        private static bool GitRename(
            ConsoleHelper console,
            FileSystemInfo renamedNode,
            DirectoryInfo containingDirectory,
            string originalSubstring,
            string newSubstring,
            string rootPath )
        {
            var newName = renamedNode.Name;

            if ( renamedNode.Name.Contains( originalSubstring, StringComparison.Ordinal ) )
            {
                newName = newName.Replace( originalSubstring, newSubstring, StringComparison.Ordinal );

                var pathFrom = renamedNode.FullName;
                var pathTo = Path.Combine( containingDirectory.FullName, newName );

                console.WriteMessage( $"Renaming: '{pathFrom}' -> '{pathTo}'" );

                if ( pathFrom == rootPath )
                {
                    Directory.Move( pathFrom, pathTo );
                }
                else
                {
                    if ( !ToolInvocationHelper.InvokeTool(
                            console,
                            "git",
                            $"mv \"{pathFrom}\" \"{pathTo}\"",
                            rootPath ) )
                    {
                        return false;
                    }
                }
            }

            if ( newName.Contains( originalSubstring, StringComparison.OrdinalIgnoreCase ) )
            {
                console.WriteWarning( $"Other casings found when renaming '{renamedNode.Name}' to '{newName}'." );
            }

            return true;
        }
    }
}