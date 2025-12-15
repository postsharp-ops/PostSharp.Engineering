// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Utilities;

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
            if ( !TeamCitySettingsFile.TryWrite( context, settings ) )
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

            if ( !image.WriteDockerfile( context ) )
            {
                return false;
            }

            // Generate Claude Dockerfile (will auto-add NodeJs if not present)
            if ( !image.WriteClaudeDockerfile( context ) )
            {
                return false;
            }
        }

        context.Console.WriteSuccess( "Generating build scripts was successful." );

        return true;
    }
}