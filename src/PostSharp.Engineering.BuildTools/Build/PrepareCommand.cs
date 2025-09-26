// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Helpers;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build;

[UsedImplicitly]
internal class PrepareCommand : BaseCommand<BuildSettings>
{
    protected override bool ExecuteCore( BuildContext context, BuildSettings settings ) => Execute( context, settings, out _ );

    public static bool Execute(
        BuildContext context,
        BuildSettings settings,
        [NotNullWhen( true )] out DependenciesConfigurationFile? dependenciesOverrideFile )
    {
        if ( !settings.NoDependencies )
        {
            if ( !CleanCommand.Execute( context, settings ) )
            {
                dependenciesOverrideFile = null;

                return false;
            }
        }

        var product = context.Product;

        if ( settings.BuildConfiguration == BuildConfiguration.Public && !context.IsContinuousIntegrationBuild && !settings.Force )
        {
            context.Console.WriteError(
                "Cannot prepare a public configuration on a development machine without --force because it may corrupt the package cache." );

            dependenciesOverrideFile = null;

            return false;
        }

        // Prepare the versions file.
        if ( !MasterGenerator.TryWriteFiles( context, settings, out dependenciesOverrideFile ) )
        {
            return false;
        }

        // Restore source dependencies.
        if ( !SourceDependenciesHelper.RestoreSourceDependencies( context ) )
        {
            return false;
        }
        
        // Create the dump directory because TeamCity does not like empty directories.
        var dumpDirectory = Path.Combine( context.RepoDirectory, product.DumpDirectory );
        Directory.CreateDirectory( dumpDirectory );
        File.WriteAllText( Path.Combine( dumpDirectory, ".empty" ), "This file is intentionally empty." );

        // Execute the event.
        product.OnPrepareCompleted( new PrepareCompletedEventArgs( context, settings ) );

        if ( !ArtifactManifestFile.TryRead( context, settings.BuildConfiguration, out var artifactManifestVersionInfo ) )
        {
            return false;
        }

        context.Console.WriteSuccess(
            $"Preparing the build was successful. {product.ProductNameWithoutDot}Version={artifactManifestVersionInfo.PackageVersion}" );

        return true;
    }
}