// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Utilities;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

[UsedImplicitly]
internal class GenerateScriptsCommand : BaseCommand<CommonCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, CommonCommandSettings settings ) => Execute( context, settings );

    public static bool Execute( BuildContext context, CommonCommandSettings settings )
    {
        var product = context.Product;

        // TeamCity
        if ( product.GenerateTeamCitySettings )
        {
            if ( !TeamCitySettingsFile.TryWrite( context ) )
            {
                return false;
            }
        }

        EmbeddedResourceHelper.ExtractScript( context, "Build.ps1", "" );
        EmbeddedResourceHelper.ExtractScript( context, "build.sh", "" );

        // Docker.
        if ( product.UseDocker )
        {
            EmbeddedResourceHelper.ExtractScript( context, "DockerBuild.ps1", "" );
            EmbeddedResourceHelper.ExtractScript( context, "RunClaude.ps1", "eng" );

            if ( product.GenerateDockerfiles )
            {
                var image = (ContainerRequirements) product.OverriddenBuildAgentRequirements!;

                // Generate the main image chain (build [+ the Visual Studio layer] + claude leaf).
                if ( !( image with { GenerateClaudeImage = true } ).WriteDockerfiles(
                        context,
                        additionalName: null,
                        extraComponents: [],
                        validateBuildComponents: true ) )
                {
                    return false;
                }

                // Generate a chain per additional Dockerfile.
                foreach ( var additionalDockerfile in product.AdditionalDockerfiles )
                {
                    var additionalImage = additionalDockerfile.Requirements ?? image;

                    if ( !additionalImage.WriteDockerfiles(
                            context,
                            additionalDockerfile.Name,
                            additionalDockerfile.Components,
                            validateBuildComponents: false ) )
                    {
                        return false;
                    }
                }
            }

            // Generate DockerMounts.g.ps1 to define additional mount points for dependencies.
            WriteDockerMounts( context );
        }

        context.Console.WriteSuccess( "Generating build scripts was successful." );

        return true;
    }

    /// <summary>
    /// Writes <c>eng/DockerMounts.g.ps1</c>, which maps the local directory of each dependency to a mount point of the
    /// build container. This file and the <c>Versions.*.g.props</c> file written with it are both excluded from source
    /// control and hold machine-local paths, so a problem here is reported as a warning and never fails the command.
    /// The tracked files written by <see cref="Execute"/> are its contract; this one is a local convenience, and
    /// commands such as the upstream merge regenerate the tracked files in a checkout where the dependencies have
    /// deliberately not been fetched.
    /// </summary>
    private static void WriteDockerMounts( BuildContext context )
    {
        const string skipped = "Skipping the generation of 'DockerMounts.g.ps1'";

        var buildSettings = new BuildSettings { BuildConfiguration = BuildConfiguration.Debug };
        buildSettings.Initialize( context );

        if ( !DependenciesConfigurationFile.TryLoad( context, buildSettings, buildSettings.BuildConfiguration, out var dependenciesOverrideFile ) )
        {
            context.Console.WriteWarning( $"{skipped}: the dependency configuration could not be read." );

            return;
        }

        // Writing DockerMounts.g.ps1 needs a resolved VersionFile for every non-feed dependency.
        // We do not fetch automatically here; the user is expected to have run 'dependencies fetch' first.
        var unfetchedDependencies = GetUnfetchedDependencies( dependenciesOverrideFile.Dependencies );

        if ( unfetchedDependencies.Length > 0 )
        {
            context.Console.WriteWarning(
                $"{skipped}: dependencies have not been fetched: {string.Join( ", ", unfetchedDependencies )}. Run './Build.ps1 dependencies fetch' first." );

            return;
        }

        if ( !dependenciesOverrideFile.TryWrite( context ) )
        {
            context.Console.WriteWarning( $"{skipped}: the file could not be written." );
        }
    }

    /// <summary>
    /// Returns the keys of the dependencies that have been neither fetched nor restored, i.e. whose version file is
    /// unknown. Feed dependencies are excluded because they are consumed from a package feed and have no local directory.
    /// </summary>
    internal static ImmutableArray<string> GetUnfetchedDependencies( IEnumerable<KeyValuePair<string, DependencySource>> dependencies )
        => dependencies
            .Where( d => d.Value.SourceKind != DependencySourceKind.Feed && d.Value.VersionFile == null )
            .Select( d => d.Key )
            .ToImmutableArray();
}