// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build.Helpers;

internal static class SourceDependenciesHelper
{
    internal static bool RestoreSourceDependencies( BuildContext context )
    {
        var product = context.Product;
        var console = context.Console;

        if ( product.SourceDependencies.Length == 0 )
        {
            return true;
        }

        var sourceDependenciesDirectory = Path.Combine( context.RepoDirectory, "source-dependencies" );

        if ( !Directory.Exists( sourceDependenciesDirectory ) )
        {
            Directory.CreateDirectory( sourceDependenciesDirectory );
        }

        foreach ( var dependency in product.SourceDependencies )
        {
            console.WriteMessage( $"Restoring '{dependency.Name}' source dependency." );

            var localDirectory = Path.Combine( context.RepoDirectory, "..", dependency.Name );

            var targetDirectory = Path.Combine( sourceDependenciesDirectory, dependency.Name );

            if ( Directory.Exists( localDirectory ) )
            {
                if ( !Directory.Exists( targetDirectory ) )
                {
                    if ( context.IsContinuousIntegrationBuild )
                    {
                        console.WriteError( "Cannot restore source dependencies in a Docker container. Prepare the build in the host first." );

                        return false;
                    }

                    console.WriteMessage( $"Creating symbolic link to '{localDirectory}' in '{targetDirectory}'." );
                    Directory.CreateSymbolicLink( targetDirectory, localDirectory );

                    if ( !Directory.Exists( targetDirectory ) )
                    {
                        console.WriteError( $"Symbolic link was not created for '{targetDirectory}'." );

                        return false;
                    }
                }
                else
                {
                    console.WriteMessage( $"Directory '{targetDirectory}' already exists." );
                }
            }
            else
            {
                if ( !Directory.Exists( targetDirectory ) )
                {
                    if ( context.IsContinuousIntegrationBuild )
                    {
                        // Avoid creating a mess in the host.
                        console.WriteError( "Cannot restore source dependencies in a Docker container. Prepare the build in the host first." );

                        return false;
                    }

                    // If the target directory doesn't exist, we clone it to the source-dependencies directory with depth of 1 to mitigate the impact of cloning the whole history.
                    if ( !ToolInvocationHelper.InvokeTool(
                            console,
                            "git",
                            $"clone {dependency.VcsRepository.DeveloperMachineRemoteUrl} --branch {dependency.Branch} --depth 1",
                            sourceDependenciesDirectory ) )
                    {
                        return false;
                    }
                }
                else
                {
                    console.WriteMessage( $"Directory '{targetDirectory}' already exists." );
                }
            }
        }

        return true;
    }
}