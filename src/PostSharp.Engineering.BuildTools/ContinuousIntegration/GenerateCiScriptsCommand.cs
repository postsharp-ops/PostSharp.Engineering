// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

[UsedImplicitly]
public class GenerateCiScriptsCommand : BaseCommand<CommonCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, CommonCommandSettings settings )
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
            if ( !product.GenerateTeamcityConfiguration( context, settings ) )
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