// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build
{
    /// <summary>
    /// Cleans the current repo from build artefacts.
    /// </summary>
    internal class CleanCommand : BaseCommand<BuildSettings>
    {
        protected override bool ExecuteCore( BuildContext context, BuildSettings settings )
        {
            return Execute( context, settings );
        }

        private static void ClearReadOnlyAttributes( string directory )
        {
            foreach ( var file in Directory.EnumerateFiles( directory, "*", SearchOption.AllDirectories ) )
            {
                var attributes = File.GetAttributes( file );

                if ( (attributes & FileAttributes.ReadOnly) != 0 )
                {
                    File.SetAttributes( file, attributes & ~FileAttributes.ReadOnly );
                }
            }
        }

        public static bool Execute( BuildContext context, BuildSettings settings )
        {
            // Kill processes that may hold file locks.
            ProcessKiller.KillProcessesBeforeClean( context.Console );

            var product = context.Product;

            void DeleteDirectory( string directory )
            {
                if ( Directory.Exists( directory ) )
                {
                    context.Console.WriteMessage( $"Deleting directory '{directory}'." );
                    ClearReadOnlyAttributes( directory );
                    Directory.Delete( directory, true );
                }
            }

            void CleanRecursive( string directory )
            {
                DeleteDirectory( Path.Combine( directory, "bin" ) );
                DeleteDirectory( Path.Combine( directory, "obj" ) );

                foreach ( var subdirectory in Directory.EnumerateDirectories( directory ) )
                {
                    if ( subdirectory == Path.Combine( context.RepoDirectory, product.EngineeringDirectory ) )
                    {
                        // Skip the engineering directory.
                        continue;
                    }

                    CleanRecursive( subdirectory );
                }
            }

            // Clears NuGet global-packages cache of Metalama and PostSharp.Engineering packages to prevent using old or corrupted package.
            void CleanNugetCache()
            {
                // Kill the processes to release the locks on the NuGet cache.
                ProcessKiller.KillWellKnownProcesses( context.Console );

                // Use dotnet command to locate nuget cache directory.
                var success = ToolInvocationHelper.InvokeTool(
                    context.Console,
                    "dotnet",
                    "nuget locals global-packages -l",
                    context.RepoDirectory,
                    out _,
                    out var output );

                if ( !success )
                {
                    context.Console.WriteWarning( "Couldn't locate NuGet cache directory, skipping cleaning it." );

                    return;
                }

                // Get only directory location string.
                var nugetCacheDirectory = output.Split( ' ' )[1].Trim();
                var directoryInfo = new DirectoryInfo( nugetCacheDirectory );

                // Delete all cached packages directories starting with 'Metalama'.
                foreach ( var dir in directoryInfo.EnumerateDirectories( "metalama*" ) )
                {
                    DeleteDirectory( Path.Combine( nugetCacheDirectory, dir.Name ) );
                }

                // Delete all cached packages directories starting with 'PostSharp.Engineering' but the current one.
                foreach ( var dir in directoryInfo.EnumerateDirectories( "postsharp.engineering*" ) )
                {
                    foreach ( var subDir in dir.EnumerateDirectories() )
                    {
                        var directoryPath = Path.Combine( nugetCacheDirectory, dir.Name, subDir.Name );

                        if ( subDir.Name.Equals( VersionHelper.EngineeringVersion, StringComparison.OrdinalIgnoreCase ) )
                        {
                            context.Console.WriteMessage( $"Skipping directory '{directoryPath}'." );

                            continue;
                        }

                        DeleteDirectory( directoryPath );
                    }
                }
            }

            // NugetCache must be automatically deleted only on TeamCity.
            if ( context is { IsContinuousIntegrationBuild: true, IsRunningUnderContainer: false } && !settings.NoNuGetCacheCleanup )
            {
                context.Console.WriteHeading( "Cleaning NuGet cache" );
                context.Console.WriteMessage( "The NuGet cache cleanup can be skipped using --no-nuget-cache-cleanup." );

                CleanNugetCache();
            }

            context.Console.WriteHeading( $"Cleaning {product.ProductName}" );

            foreach ( var directory in product.AdditionalDirectoriesToClean )
            {
                DeleteDirectory( Path.Combine( context.RepoDirectory, directory ) );
            }

            DeleteDirectory( product.GetPrivateArtifactsAbsoluteDirectory( context, settings.BuildConfiguration ) );

            DeleteDirectory( product.GetPublicArtifactsAbsoluteDirectory( context ) );

            DeleteDirectory(
                Path.Combine(
                    context.RepoDirectory,
                    product.LogsDirectory ) );

            foreach ( var directory in Directory.GetDirectories( context.RepoDirectory ) )
            {
                switch ( Path.GetFileName( directory ) )
                {
                    case "source-dependencies":
                    case "dependencies":
                    case ".sonarqube":
                    case { } s when s == product.EngineeringDirectory:
                        continue;

                    default:
                        CleanRecursive( directory );

                        break;
                }
            }

            return true;
        }
    }
}