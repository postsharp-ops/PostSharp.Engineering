// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using System;
using System.IO;

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

        var dockerBuildScriptPath = Path.Combine( context.RepoDirectory, "DockerBuild.ps1" );
        
        if ( product.UseDocker )
        {
            // Extract DockerBuild.ps1.
            using var resource = this.GetType().Assembly.GetManifestResourceStream( "PostSharp.Engineering.BuildTools.Resources.DockerBuild.ps1" )
                                 ?? throw new InvalidOperationException( "Cannot find the resource DockerBuild.ps1." );
            
            using var reader = new StreamReader( resource );
            var script = reader.ReadToEnd();
            script = script.Replace( "<ENG_PATH>", product.EngineeringDirectory, StringComparison.Ordinal );

            if ( !File.Exists( dockerBuildScriptPath ) || File.ReadAllText( dockerBuildScriptPath ) != script )
            {
                context.Console.WriteMessage( $"Writing '{dockerBuildScriptPath}'." );

                File.WriteAllText( dockerBuildScriptPath, script );
            }
        }

        return true;
    }
}