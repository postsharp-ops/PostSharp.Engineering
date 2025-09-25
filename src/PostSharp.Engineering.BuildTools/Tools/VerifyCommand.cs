// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Publishing;
using PostSharp.Engineering.BuildTools.Tools.NuGet;

namespace PostSharp.Engineering.BuildTools.Tools;

[UsedImplicitly]
internal class VerifyCommand : BaseCommand<PublishSettings>
{
    protected override bool ExecuteCore( BuildContext context, PublishSettings settings ) => Execute( context, settings );

    public static bool Execute( BuildContext context, PublishSettings settings )
    {
        var product = context.Product;
        var configuration = settings.BuildConfiguration;

        if ( configuration == BuildConfiguration.Public )
        {
            var buildArguments = BuildArguments.Read( context, configuration );
            var directories = product.GetArtifactsDirectories( context );

            // Verify that public packages have no private dependencies.
            if ( !VerifyPublicPackageCommand.Execute(
                    context.Console,
                    new VerifyPublicPackageCommandSettings { Directory = directories.Public } ) )
            {
                return false;
            }

            return true;
        }
        else
        {
            context.Console.WriteError( "Artifacts can only be verified for the public build." );

            return false;
        }
    }
}