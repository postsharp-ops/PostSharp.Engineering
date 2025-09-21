// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Helpers;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
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

        public static bool Execute( BuildContext context, PublishSettings settings )
        {
            context.Console.WriteHeading( "Finishing publishing." );

            if ( !MasterGenerator.TryWriteFiles( context, settings, out _ ) )
            {
                return false;
            }

            if ( TeamCityHelper.IsTeamCityBuild( settings ) )
            {
                // When on TeamCity, Git user credentials are set to TeamCity.
                if ( !TeamCityHelper.TrySetGitIdentityCredentials( context ) )
                {
                    return false;
                }
            }

            var product = context.Product;
            var sourceBranch = product.DependencyDefinition.ReleaseBranch;

            if ( sourceBranch == null )
            {
                context.Console.WriteError( $"Post-publishing failed. The release branch is not set for '{product.ProductName}' product." );

                return false;
            }

            if ( context.Branch != sourceBranch )
            {
                context.Console.WriteError(
                    $"Post-publishing can only be executed on the release branch ('{sourceBranch}'). The current branch is '{context.Branch}'." );

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