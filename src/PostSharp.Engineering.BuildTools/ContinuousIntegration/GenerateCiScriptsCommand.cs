// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
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
            ExtractScript( "DockerBuild.ps1", "" );
            ExtractScript( "ReadSecrets.ps1", Path.Combine( product.EngineeringDirectory, "docker-context" ) );
        }

        return true;

        void ExtractScript( string fileName, string targetDirectory )
        {
            var targetPath = Path.Combine( context.RepoDirectory, targetDirectory, fileName );

            using var resource = this.GetType().Assembly.GetManifestResourceStream( $"PostSharp.Engineering.BuildTools.Resources.{fileName}" )
                                 ?? throw new InvalidOperationException( $"Cannot find the resource {fileName}." );

            using var reader = new StreamReader( resource );
            var script = reader.ReadToEnd();

            script = script
                .Replace( "<ENG_PATH>", product.EngineeringDirectory, StringComparison.Ordinal )
                .Replace( "<ENVIRONMENT_VARIABLES>", string.Join( ",", EnvironmentVariableNames.All.OrderBy( x => x ) ), StringComparison.Ordinal );

            if ( !File.Exists( targetPath ) || File.ReadAllText( targetPath ) != script )
            {
                context.Console.WriteMessage( $"Writing '{targetPath}'." );

                File.WriteAllText( targetPath, script );
            }
        }
    }
}