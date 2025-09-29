// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Dependencies;
using PostSharp.Engineering.BuildTools.Tools.Git;
using PostSharp.Engineering.BuildTools.Utilities;

namespace PostSharp.Engineering.BuildTools.Build.Publishing;

/// <summary>
/// Prepares publishing (deployment) of the artifacts to feeds, marketplaces, or deployment slots.
/// </summary>
[UsedImplicitly]
internal class PrePublishCommand : BaseCommand<PublishSettings>
{
    protected override bool ExecuteCore( BuildContext context, PublishSettings settings ) => Execute( context, settings );

    public static bool Execute( BuildContext context, PublishSettings settings )
    {
        var product = context.Product;

        if ( !GitHelper.TryConfigureCredentials( context ) )
        {
            return false;
        }
        
        if ( product.ProductFamily.UpstreamProductFamily != null && !DownstreamMerge.CheckUpstreamChanges( context, settings ) )
        {
            return false;
        }

        // Check that we're ready to publish.
        if ( !PublishCommand.CanPublish( context, settings ) )
        {
            return false;
        }

        var sourceBranch = context.Product.DependencyDefinition.Branch;

        if ( context.Branch != sourceBranch )
        {
            context.Console.WriteError(
                $"Pre-publishing can only be executed on the development branch ('{sourceBranch}'). The current branch is '{context.Branch}'." );

            return false;
        }

        var targetBranch = context.Product.DependencyDefinition.ReleaseBranch;

        if ( targetBranch == null )
        {
            context.Console.WriteError( $"Pre-publishing failed. The release branch is not set for '{context.Product.ProductName}' product." );

            return false;
        }

        if ( !AutoUpdatedDependenciesHelper.TryUpdateAutoUpdatedDependencies( context, settings ) )
        {
            context.Console.WriteError( "Failed to update auto-updated dependencies." );

            return false;
        }

        if ( !GitHelper.TryPullAndMergeAndPush( context, settings, targetBranch ) )
        {
            return false;
        }

        // Act as a local dependency for subsequent projects, that use the --use-local-dependencies flag.
        ImportFile.Write( context, settings.BuildConfiguration );

        return true;
    }
}