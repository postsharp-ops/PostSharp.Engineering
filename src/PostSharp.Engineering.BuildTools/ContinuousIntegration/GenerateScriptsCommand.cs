// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

[UsedImplicitly]
internal class GenerateScriptsCommand : BaseCommand<CommonCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, CommonCommandSettings settings ) => Execute( context, settings );
    
    public static bool Execute( BuildContext context, CommonCommandSettings settings )
    {
        var product = context.Product;

        if ( product.IsBundle )
        {
            if ( !TeamCityHelper.TryGenerateConsolidatedTeamcityConfiguration( context ) )
            {
                return false;
            }
        }
        else
        {
            if ( !TeamCitySettingsFile.TryWrite( context, settings ) )
            {
                return false;
            }
        }

        if ( product.UseDocker )
        {
            EmbeddedResourceHelper.ExtractScript( context, "DockerBuild.ps1", "" );
            var image = (ContainerRequirements) product.OverriddenBuildAgentRequirements!;

            if ( !image.Prepare( context ) )
            {
                return false;
            }
        }

        context.Console.WriteSuccess( "Generating build scripts was successful." );

        return true;
    }
}