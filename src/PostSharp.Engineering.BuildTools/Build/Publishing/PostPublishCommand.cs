// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Helpers;
using PostSharp.Engineering.BuildTools.Utilities;

namespace PostSharp.Engineering.BuildTools.Build.Publishing
{
    /// <summary>
    /// Finalizes the publishing after the deployment of artifacts to feeds, marketplaces, or deployment slots.
    /// </summary>
    [UsedImplicitly]
    internal class PostPublishCommand : BaseCommand<PublishSettings>
    {
        protected override bool ExecuteCore( BuildContext context, PublishSettings settings ) => Execute( context, settings );

        private static bool Execute( BuildContext context, PublishSettings settings )
        {
            context.Console.WriteHeading( "Finishing publishing." );

            var product = context.Product;
            var releaseBranch = product.DependencyDefinition.ReleaseBranch;

            if ( releaseBranch == null )
            {
                context.Console.WriteError( $"The release branch is not set for '{product.ProductName}' product." );

                return false;
            }

            if ( context.Branch != releaseBranch )
            {
                context.Console.WriteError(
                    $"Post-publishing can only be executed on the release branch ('{releaseBranch}'). The current branch is '{context.Branch}'." );

                return false;
            }

            settings.OverrideDefaultBuildConfiguration( BuildConfiguration.Public );
        
            if ( settings.BuildConfiguration != BuildConfiguration.Public && !settings.Force )
            {
                context.Console.WriteError( $"This command must be executed with the `-c Public` argument unless --force is used." );
            }
            
            if ( !GitHelper.TryConfigureCredentials( context ) )
            {
                return false;
            }

            if ( !GitIntegrationHelper.TryAddTagToLastCommit( context, settings ) )
            {
                context.Console.WriteError( "Failed to tag the latest commit." );

                return false;
            }

            // Merge the release branch back to develop branch.
            if ( !GitHelper.TryPullAndMergeAndPush( context, settings, product.DependencyDefinition.Branch ) )
            {
                return false;
            }

            // Act as a local dependency for subsequent projects, that use the --use-local-dependencies flag.
            ImportFile.Write( context, settings.BuildConfiguration );

            context.Console.WriteSuccess( "Publishing finished successfuly." );

            return true;
        }
    }
}