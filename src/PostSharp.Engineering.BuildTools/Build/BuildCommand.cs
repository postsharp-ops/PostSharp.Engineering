// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Microsoft.Extensions.FileSystemGlobbing;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Testing;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Build
{
    /// <summary>
    /// Builds the product.
    /// </summary>
    [UsedImplicitly]
    internal class BuildCommand : BaseCommand<BuildSettings>
    {
        protected override bool ExecuteCore( BuildContext context, BuildSettings settings )
        {
            if ( context.Product.TestOnBuild )
            {
                context.Console.WriteWarning( "'test' command executed instead." );

                return TestCommand.Execute( context, settings );
            }
            else
            {
                return Execute( context, settings );
            }
        }

        public static bool Execute( BuildContext context, BuildSettings settings )
        {
            var product = context.Product;
            var configuration = settings.BuildConfiguration;
            var buildConfigurationInfo = product.Configurations[configuration];

            // Skip if we have a date tag and a fresh build.
            DateTime dateTag;

            if ( settings.DateTag != null )
            {
                dateTag = DateTime.FromBinary( settings.DateTag.Value );
                var propsFile = ArtifactManifestFile.GetPath( context, settings.BuildConfiguration );

                if ( !File.Exists( propsFile ) || File.GetLastWriteTime( propsFile ) > dateTag )
                {
                    context.Console.WriteMessage( "There is already a fresh build." );

                    return true;
                }
            }
            else
            {
                dateTag = DateTime.Now;
            }

            // Build dependencies.
            DependenciesConfigurationFile? dependenciesOverrideFile;

            if ( !settings.NoDependencies )
            {
                if ( !PrepareCommand.Execute( context, settings, out dependenciesOverrideFile ) )
                {
                    return false;
                }
            }
            else
            {
                // Read the resolved dependencies.
                if ( !DependenciesConfigurationFile.TryLoad( context, settings, configuration, out dependenciesOverrideFile ) )
                {
                    return false;
                }
            }

            // If we have a recursive build, build local dependencies.
            if ( settings.Recursive )
            {
                foreach ( var dependency in dependenciesOverrideFile.Dependencies )
                {
                    if ( dependency.Value.SourceKind == DependencySourceKind.Local )
                    {
                        if ( context.Product.TryGetDependencyDefinition( dependency.Key, out var dependencyDefinition )
                             && dependencyDefinition.ExcludeFromRecursiveBuild )
                        {
                            continue;
                        }

                        context.Console.WriteHeading( $"Build dependency {dependency.Key}" );

                        var dependencyDirectory = Path.GetDirectoryName( dependency.Value.VersionFile! )!;

                        var buildFile = Path.Combine( dependencyDirectory, "Build.ps1" );

                        if ( !File.Exists( buildFile ) )
                        {
                            context.Console.WriteError( $"Cannot find '{buildFile}'." );

                            return false;
                        }

                        if ( !ToolInvocationHelper.InvokePowershell(
                                context.Console,
                                buildFile,
                                $"build --recursive --if-older={dateTag.ToBinary()} -c {settings.BuildConfiguration.ToString().ToLowerInvariant()} --nologo",
                                dependencyDirectory ) )
                        {
                            context.Console.WriteError( $"Cannot build the dependency {dependency.Key}." );

                            return false;
                        }
                    }
                }
            }

            // Delete the root import file in the repo because the presence of this file means a successful build.
            ImportFile.Delete( context );

            // We have to read the version from the file we have generated - using MSBuild, because it contains properties.
            var buildInfo = BuildArguments.Read( context, settings.BuildConfiguration );

            var privateArtifactsDirectory = product.GetPrivateArtifactsDirectory( context, buildInfo );

            // Build solutions.
            IEnumerable<Solution> solutionsToBuild;

            if ( settings.SolutionId != null )
            {
                var solution = product.Solutions[settings.SolutionId.Value - 1];
                solutionsToBuild = [solution];
            }
            else
            {
                solutionsToBuild = product.Solutions;
            }

            foreach ( var solution in solutionsToBuild )
            {
                if ( settings.IncludeTests || !solution.IsTestOnly )
                {
                    context.Console.WriteHeading( $"Building {solution.Name} ({settings.BuildConfiguration} configuration)" );

                    if ( !settings.NoDependencies )
                    {
                        if ( !solution.Restore( context, settings ) )
                        {
                            return false;
                        }
                    }

                    var buildMethod = solution.GetBuildMethod();

                    if ( !solution.Execute( context, settings, buildMethod ) )
                    {
                        return false;
                    }

                    context.Console.WriteSuccess( $"Building {solution.Name} was successful." );
                }
            }

            var publicArtifactsDirectory = product.GetPublicArtifactsDirectory( context, buildInfo );

            // Allow for some customization before we create the zip file and copy to the public directory.
            var eventArgs = new BuildCompletedEventArgs( context, settings, buildInfo, privateArtifactsDirectory, publicArtifactsDirectory );
            product.OnBuildCompleted( eventArgs );

            if ( eventArgs.IsFailed )
            {
                return false;
            }

            // Check that the build produced the expected artifacts.
            var allFilesPattern = product.PublicArtifacts.Append( product.PrivateArtifacts );

            if ( !allFilesPattern.Verify( context, privateArtifactsDirectory, buildInfo ) )
            {
                return false;
            }

            // Zipping internal artifacts.
            void CreateZip( string directory )
            {
                if ( settings.CreateZip )
                {
                    var zipFile = Path.Combine( directory, $"{product.ProductName}-{buildInfo.PackageVersion}.zip" );

                    context.Console.WriteMessage( $"Creating '{zipFile}'." );
                    var tempFile = Path.Combine( Path.GetTempPath(), Guid.NewGuid() + ".zip" );

                    ZipFile.CreateFromDirectory(
                        directory,
                        tempFile,
                        CompressionLevel.Optimal,
                        false );

                    File.Move( tempFile, zipFile );
                }
            }

            CreateZip( privateArtifactsDirectory );

            // Copy public artifacts to the publish directory.
            if ( !Directory.Exists( publicArtifactsDirectory ) )
            {
                Directory.CreateDirectory( publicArtifactsDirectory );
            }

            void CreateEmptyPublicDirectory()
            {
                // We have to create an empty file, otherwise TeamCity will complain that
                // artifacts are missing.
                var emptyFile = Path.Combine( publicArtifactsDirectory, ".empty" );

                File.WriteAllText( emptyFile, "This file is intentionally empty." );
            }

            if ( product.PublicArtifacts.IsEmpty )
            {
                context.Console.WriteMessage( "Do not prepare public artifacts because there is none." );
                CreateEmptyPublicDirectory();
            }
            else if ( settings.BuildConfiguration != BuildConfiguration.Public )
            {
                context.Console.WriteMessage( "Do not prepare public artifacts because this is not a public build" );
                CreateEmptyPublicDirectory();
            }
            else
            {
                // Copy artifacts.
                context.Console.WriteHeading( "Copying public artifacts" );
                var filePatternMatches = new List<FilePatternMatch>();

                product.PublicArtifacts.TryGetFiles( privateArtifactsDirectory, buildInfo, filePatternMatches );
                IEnumerable<string> files = filePatternMatches.Select( m => m.Path ).ToArray();

                // Automatically include respective symbol NuGet packages.
                files = files.Concat(
                    files.Where( f => f.EndsWith( ".nupkg", StringComparison.OrdinalIgnoreCase ) )
                        .Select( f => f[..^".nupkg".Length] + ".snupkg" )
                        .Where( f => File.Exists( Path.Combine( privateArtifactsDirectory, f ) ) ) );

                foreach ( var file in files )
                {
                    var targetFile = Path.Combine( publicArtifactsDirectory, Path.GetFileName( file ) );

                    context.Console.WriteMessage( file );
                    File.Copy( Path.Combine( privateArtifactsDirectory, file ), targetFile, true );
                }

                var signSuccess = true;

                if ( buildConfigurationInfo.RequiresSigning && !settings.NoSign )
                {
                    context.Console.WriteHeading( "Signing artifacts" );

                    var signToolSecret = Environment.GetEnvironmentVariable( EnvironmentVariableNames.SignServerSecret );

                    if ( signToolSecret == null )
                    {
                        context.Console.WriteError( "The SIGNSERVER_SECRET environment variable is not defined." );

                        return false;
                    }

                    void Sign( string filter )
                    {
                        if ( Directory.EnumerateFiles( publicArtifactsDirectory, filter ).Any() )
                        {
                            signSuccess = signSuccess && DotNetTool.SignClient.Invoke(
                                context,
                                $"Sign --baseDirectory \"{publicArtifactsDirectory}\" --input {filter}" );
                        }
                    }

                    Sign( "*.nupkg" );
                    Sign( "*.snupkg" );
                    Sign( "*.vsix" );

                    if ( !signSuccess )
                    {
                        return false;
                    }

                    // Zipping public artifacts.
                    CreateZip( publicArtifactsDirectory );

                    context.Console.WriteSuccess( "Signing artifacts was successful." );
                }
            }

            // Create the consolidate directory.
            if ( settings.CreateConsolidatedDirectory )
            {
                context.Console.WriteHeading( "Creating the consolidated directory" );

                var consolidatedDirectory = Path.Combine(
                    context.RepoDirectory,
                    "artifacts",
                    "consolidated",
                    configuration.ToString().ToLowerInvariant() );

                if ( Directory.Exists( consolidatedDirectory ) )
                {
                    Directory.Delete( consolidatedDirectory, true );
                }

                Directory.CreateDirectory( consolidatedDirectory );

                context.Console.WriteMessage( $"Creating '{consolidatedDirectory}'." );

                // Copy dependencies.

                foreach ( var dependency in dependenciesOverrideFile.Dependencies )
                {
                    if ( dependency.Value.VersionFile != null )
                    {
                        var versionDocument = XDocument.Load( dependency.Value.VersionFile );
                        var import = versionDocument.Root!.Element( "Import" )?.Attribute( "Project" )?.Value;

                        string importDirectory;

                        if ( import == null )
                        {
                            importDirectory = Path.GetDirectoryName( dependency.Value.VersionFile )!;
                        }
                        else
                        {
                            importDirectory = Path.GetDirectoryName( Path.Combine( Path.GetDirectoryName( dependency.Value.VersionFile )!, import ) )!;
                        }

                        CopyPackages( importDirectory );
                    }
                }

                // Copy current repo.
                CopyPackages( privateArtifactsDirectory );

                void CopyPackages( string directory )
                {
                    foreach ( var file in Directory.GetFiles( directory, "*.nupkg" ).Concat( Directory.GetFiles( directory, "*.snupkg" ) ) )
                    {
                        File.Copy( file, Path.Combine( consolidatedDirectory, Path.GetFileName( file ) ), true );
                    }
                }
            }

            // Writing the import file at the end of the build so it gets only written if the build was successful.
            ImportFile.Write( context, configuration );

            product.OnArtifactsPrepared( eventArgs );

            context.Console.WriteSuccess( $"Building the whole {product.ProductName} product was successful. Package version: {buildInfo.PackageVersion}." );

            return true;
        }
    }
}