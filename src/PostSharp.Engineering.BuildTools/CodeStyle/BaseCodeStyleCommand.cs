// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Utilities;
using System.IO;

namespace PostSharp.Engineering.BuildTools.CodeStyle
{
    /// <summary>
    /// Base class for <see cref="PushCodeStyleCommand"/> and <see cref="PullCodeStyleCommand"/>.
    /// </summary>
    internal abstract class BaseCodeStyleCommand<T> : BaseCommand<T>
        where T : CodeStyleSettings
    {
        protected static string? GetCodeStyleRepo( BuildContext context, CodeStyleSettings settings )
        {
            var sharedRepo = Path.GetFullPath( Path.Combine( context.RepoDirectory, "..", "PostSharp.Engineering.CodeStyle" ) );
            var console = context.Console;

            // Check if the repo exists.
            if ( !Directory.Exists( sharedRepo ) )
            {
                if ( !settings.Create )
                {
                    console.WriteError( $"The directory '{sharedRepo} does not exist. Use --create'" );

                    return null;
                }
                else
                {
                    var baseDir = Path.GetDirectoryName( sharedRepo )!;

                    ToolInvocationHelper.InvokeTool( console, "git", $"clone {settings.Url}", baseDir );
                }
            }

            // Check that there is no uncommitted change in the target repo.
            if ( !GitHelper.CheckNoChange( context, settings, sharedRepo ) )
            {
                return null;
            }

            // Get product specific codestyle.
            sharedRepo = Path.Combine( sharedRepo, context.Product.DependencyDefinition.CodeStyle );

            return sharedRepo;
        }

        protected static void CopyDirectory( string source, string target )
        {
            // Delete the target directory, except .git.
            if ( Directory.Exists( target ) )
            {
                foreach ( var existingFile in Directory.GetFiles( target ) )
                {
                    File.Delete( existingFile );
                }

                foreach ( var existingDirectory in Directory.GetDirectories( target ) )
                {
                    var shortName = Path.GetFileName( existingDirectory );

                    if ( shortName != ".git" && shortName != "bin" && shortName != "obj" )
                    {
                        Directory.Delete( existingDirectory, true );
                    }
                }
            }

            // Copy files.
            CopyRecursive( source, target );

            static void CopyRecursive( string sourceSubdirectory, string targetSubdirectory )
            {
                if ( !Directory.Exists( targetSubdirectory ) )
                {
                    Directory.CreateDirectory( targetSubdirectory );
                }

                foreach ( var file in Directory.GetFiles( sourceSubdirectory ) )
                {
                    var shortName = Path.GetFileName( file );
                    File.Copy( file, Path.Combine( targetSubdirectory, shortName ), true );
                }

                foreach ( var directory in Directory.GetDirectories( sourceSubdirectory ) )
                {
                    var shortName = Path.GetFileName( directory );

                    if ( shortName != ".git" )
                    {
                        CopyRecursive( directory, Path.Combine( targetSubdirectory, shortName ) );
                    }
                }
            }
        }
    }
}