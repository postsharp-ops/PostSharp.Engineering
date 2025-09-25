// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Helpers;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Swapping;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;

namespace PostSharp.Engineering.BuildTools.Build.Publishing;

/// <summary>
/// Publishes (deploys) the artifacts to feeds, marketplaces, or deployment slots.
/// </summary>
[UsedImplicitly]
internal class PublishCommand : BaseCommand<PublishSettings>
{
    protected override bool ExecuteCore( BuildContext context, PublishSettings settings ) => Execute( context, settings );

    internal static bool CanPublish( BuildContext context, PublishSettings settings )
    {
        var product = context.Product;

        if ( !MainVersionFile.TryRead( context, out var mainVersionFileInfo, out _ ) )
        {
            return false;
        }

        if ( !ArtifactManifestFile.TryRead(
                context,
                settings.BuildConfiguration,
                out var preparedVersionInfo ) )
        {
            return false;
        }

        // Only versioned products require version bump.
        if ( product.DependencyDefinition.IsVersioned )
        {
            // Analyze the repository state since the last deployment.
            if ( !GitIntegrationHelper.TryAnalyzeGitHistory(
                    context,
                    mainVersionFileInfo,
                    out var hasBumpSinceLastDeployment,
                    out var hasChangesSinceLastDeployment,
                    out var lastVersionTag ) )
            {
                return false;
            }

            // If there are no changes since the deployment, we get only a warning and deployment proceeds with the same version.
            if ( !hasChangesSinceLastDeployment )
            {
                context.Console.WriteWarning( $"There are no new unpublished changes since the last deployment." );
            }
            else
            {
                // To check if version was bumped manually we get full prepared version info.
                var currentVersion = preparedVersionInfo.PackageVersion;

                // Publishing fails if there are changes and the version has not been bumped since the last deployment.
                if ( !hasBumpSinceLastDeployment && currentVersion == lastVersionTag )
                {
                    context.Console.WriteError( "There are changes since the last deployment but the version has not been bumped." );

                    return false;
                }
            }
        }

        return true;
    }

    public static bool Execute( BuildContext context, PublishSettings settings )
    {
        var product = context.Product;
        context.Console.WriteHeading( "Publishing files" );

        if ( !context.Product.IsPublishingNonReleaseBranchesAllowed && !settings.IsStandalone )
        {
            var publishingBranch = context.Product.DependencyDefinition.PublishingBranch;

            if ( context.Branch != publishingBranch )
            {
                context.Console.WriteError( $"Publishing can only be executed on the '{publishingBranch}' branch. The current branch is '{context.Branch}'." );

                return false;
            }
        }

        if ( !CanPublish( context, settings ) )
        {
            return false;
        }

        if ( !GitHelper.ConfigureCredentials( context ) )
        {
            context.Console.WriteError( "Cannot configure git credentials." );

            return false;
        }

        if ( !MasterGenerator.TryWriteFiles( context, settings ) )
        {
            return false;
        }

        // TODO: Verification is broken - NuGet verification is slow and makes the verification fail
        // on seemimngly unpublished packages.
        // if ( settings.BuildConfiguration == BuildConfiguration.Public )
        // {
        //     if ( !product.Verify( context, settings ) )
        //     {
        //         return false;
        //     }
        // }

        var configuration = settings.BuildConfiguration;
        var buildArguments = BuildArguments.Read( context, configuration );
        var directories = product.GetArtifactsAbsoluteDirectories( context, configuration );
        var configurationInfo = product.Configurations.GetValue( configuration );
        var hasTarget = false;

        if ( !Publisher.PublishDirectory(
                context,
                settings,
                directories,
                configurationInfo,
                buildArguments,
                false,
                ref hasTarget ) )
        {
            return false;
        }

        if ( !Publisher.PublishDirectory(
                context,
                settings,
                directories,
                configurationInfo,
                buildArguments,
                true,
                ref hasTarget ) )
        {
            return false;
        }

        // For consolidated deployments, this is part of the post-deployment step.
        if ( !product.ProductFamily.HasConsolidatedBuild && !settings.IsStandalone )
        {
            if ( context.IsContinuousIntegrationBuild )
            {
                // When on TeamCity, Git user credentials are set to TeamCity.
                if ( !TeamCityHelper.TrySetGitIdentityCredentials( context ) )
                {
                    return false;
                }
            }

            if ( !GitIntegrationHelper.TryAddTagToLastCommit( context, settings ) )
            {
                context.Console.WriteError( "Failed to tag the latest commit." );

                return false;
            }

            var releaseBranch = context.Product.DependencyDefinition.ReleaseBranch;

            if ( releaseBranch != null && context.Branch == context.Product.DependencyDefinition.Branch )
            {
                if ( settings.Dry )
                {
                    context.Console.WriteImportantMessage( $"Dry run: Merging the current branch to '{releaseBranch}' branch." );
                }
                else if ( !GitHelper.TryPullAndMergeAndPush( context, settings, releaseBranch ) )
                {
                    return false;
                }
            }
        }

        if ( !hasTarget )
        {
            context.Console.WriteWarning( "No active publishing target was detected." );
        }
        else
        {
            context.Console.WriteSuccess( "Publishing has succeeded." );
        }

        // Swap after successful publishing.
        if ( configurationInfo.SwapAfterPublishing )
        {
            context.Console.WriteMessage( "Swapping staging and production slots after publishing." );

            if ( !SwapCommand.ExecuteAfterPublishing( context, settings ) )
            {
                context.Console.WriteError( "Failed to swap after publishing." );

                return false;
            }

            context.Console.WriteSuccess( "Swap after publishing has succeeded." );
        }

        return true;
    }
}