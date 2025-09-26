// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System.Diagnostics.CodeAnalysis;

namespace PostSharp.Engineering.BuildTools.Build.Files;

internal static class MasterGenerator
{
    public static bool TryWriteFiles(
        BuildContext context,
        BuildSettings settings )
        => TryWriteFiles( context, settings, out _ );

    public static bool TryWriteFiles(
        BuildContext context,
        BuildSettings settings,
        [NotNullWhen( true )] out DependenciesConfigurationFile? dependenciesOverrideFile )
    {
        var configuration = settings.BuildConfiguration;

        context.Console.WriteHeading( "Preparing version files." );

        var propsFilePath = ArtifactManifestFile.GetPath( context, settings.BuildConfiguration );

        // Load Versions.<Configuration>.g.props.
        if ( !DependenciesConfigurationFile.TryLoad( context, settings, configuration, out dependenciesOverrideFile ) )
        {
            return false;
        }

        // If we have any non-feed dependency that does not have a resolved VersionFile, it means that we have not fetched yet. 
        if ( !dependenciesOverrideFile.Fetch( context ) )
        {
            return false;
        }

        // Validate Versions.props. We should not have conditional properties.
        if ( !VersionFile.Validate( context, dependenciesOverrideFile ) )
        {
            return false;
        }

        // We always save the Versions.g.props because it may not exist, and it may have been changed by the previous step.
        dependenciesOverrideFile.LocalBuildFile = propsFilePath;

        if ( !dependenciesOverrideFile.TryWrite( context ) )
        {
            return false;
        }

        if ( !MainVersionFile.TryRead( context, out var mainVersionFileInfo, out _ ) )
        {
            return false;
        }

        if ( !VersionFileHelper.TryComputeVersion( context, settings, configuration, mainVersionFileInfo, dependenciesOverrideFile, out var version ) )
        {
            return false;
        }

        if ( !GitHelper.TryGetLatestCommitDate( context, out var buildDate ) )
        {
            return false;
        }

        // Generate Versions.g.props.
        if ( !ArtifactManifestFile.TryWrite(
                version,
                configuration,
                dependenciesOverrideFile,
                context,
                settings,
                buildDate ) )
        {
            return false;
        }

        // Generate nuget.config and global.json.
        if ( !NuGetConfigFile.TryWrite( context, dependenciesOverrideFile, settings.BuildConfiguration ) ||
             !GlobalJsonFile.TryWrite( context, settings.SdkVersion ) )
        {
            return false;
        }

        // Generating the configuration-neutral Versions.g.props for the prepared configuration.
        ConfigurationNeutralVersionFile.Write( context, settings, settings.BuildConfiguration );

        return true;
    }
}