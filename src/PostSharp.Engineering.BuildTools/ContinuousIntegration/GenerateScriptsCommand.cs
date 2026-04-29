// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Utilities;
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
            var image = (ContainerRequirements) product.OverriddenBuildAgentRequirements!;

            // Generate main Dockerfile variants (standard + win2022 + claude + claude.win2022)
            if ( !image.WriteAllVariants( context, "", [] ) )
            {
                return false;
            }

            // Generate additional Dockerfile variants
            foreach ( var additionalDockerfile in product.AdditionalDockerfiles )
            {
                if ( !image.WriteAllVariants( context, additionalDockerfile.Name, additionalDockerfile.Components ) )
                {
                    return false;
                }
            }

            // Generate DockerMounts.g.ps1 to define additional mount points for dependencies
            var buildSettings = new BuildSettings { BuildConfiguration = BuildConfiguration.Debug };
            buildSettings.Initialize( context );

            if ( !DependenciesConfigurationFile.TryLoad( context, buildSettings, buildSettings.BuildConfiguration, out var dependenciesOverrideFile ) )
            {
                return false;
            }

            // Writing DockerMounts.g.ps1 needs a resolved VersionFile for every non-feed dependency.
            // We do not fetch automatically here; the user is expected to have run 'dependencies fetch' first.
            var unfetched = dependenciesOverrideFile.Dependencies
                .Where( d => d.Value.SourceKind != DependencySourceKind.Feed && d.Value.VersionFile == null )
                .Select( d => d.Key )
                .ToList();

            if ( unfetched.Count > 0 )
            {
                context.Console.WriteError(
                    $"Cannot generate scripts: dependencies have not been fetched: {string.Join( ", ", unfetched )}. Run './Build.ps1 dependencies fetch' first." );

                return false;
            }

            if ( !dependenciesOverrideFile.TryWrite( context ) )
            {
                return false;
            }
        }

        context.Console.WriteSuccess( "Generating build scripts was successful." );

        return true;
    }
}