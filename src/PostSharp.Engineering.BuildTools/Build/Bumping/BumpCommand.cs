// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Helpers;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Bumping;

[UsedImplicitly]
internal class BumpCommand : BaseCommand<BumpSettings>
{
    protected override bool ExecuteCore( BuildContext context, BumpSettings settings ) => Execute( context, settings );

    private static bool Execute( BuildContext context, BumpSettings settings )
    {
        var product = context.Product;
        var console = context.Console;

        if ( !product.DependencyDefinition.IsVersioned )
        {
            console.WriteWarning( "Ignoring the version bumping because this product is not versioned." );

            return true;
        }

        console.WriteHeading( $"Bumping the '{product.ProductName}' version" );

        var developmentBranch = product.DependencyDefinition.Branch;

        if ( context.Branch != developmentBranch )
        {
            console.WriteError(
                $"The version bump can only be executed on the development branch ('{developmentBranch}'). The current branch is '{context.Branch}'." );

            return false;
        }

        if ( !GitHelper.TryConfigureCredentials( context ) )
        {
            console.WriteError( "Cannot configure git credentials." );

            return false;
        }

        // It is forbidden to push to the release branch, but it occasionally happens.
        // We need to make sure that there are no pending changes in the release branch to be merged to the development branch.
        // Failing to do so could result in missing published changes, and it could also break the version bump.
        var releaseBranch = product.DependencyDefinition.ReleaseBranch;

        if ( releaseBranch == null )
        {
            console.WriteMessage( "Skipping check for pending changes from the release branch, as the release branch is not set for this product." );
        }
        else
        {
            console.WriteMessage( $"Checking for pending changes from the release branch ('{releaseBranch}')." );

            if ( !GitHelper.TryCheckoutAndPull( context, releaseBranch ) )
            {
                return false;
            }

            if ( !GitHelper.TryCheckoutAndPull( context, context.Branch ) )
            {
                return false;
            }

            if ( !GitHelper.TryGetCommitsCount( context, "HEAD", releaseBranch, out var count ) )
            {
                return false;
            }

            if ( count > 0 )
            {
                console.WriteError( $"There are pending changes from the '{releaseBranch}' branch." );
                console.WriteError( $"Check the relevancy of the changes and merge the '{releaseBranch}' branch to the '{developmentBranch}'." );
                console.WriteError( "Failing to do so could result in invalid version number of this product." );

                return false;
            }
        }

        if ( !MainVersionFile.TryRead( context, out var currentMainVersionFile ) )
        {
            return false;
        }

        // If the version has already been dumped since the last deployment, there is nothing to do. 
        if ( !GitIntegrationHelper.TryAnalyzeGitHistory(
                context,
                currentMainVersionFile,
                out var hasBumpSinceLastDeployment,
                out var hasChangesSinceLastDeployment,
                out _ ) )
        {
            return false;
        }

        if ( hasBumpSinceLastDeployment && !settings.OverridePreviousBump )
        {
            console.WriteWarning( "Version has already been bumped since the last deployment." );

            return true;
        }

        if ( !AutoUpdatedVersionsFile.TryRead( context, out _, out var currentVersion ) )
        {
            return false;
        }

        // Doing a dry run of AutoUpdatedVersionsFile both gets the versions of all dependencies and gets the current version.
        // Do not write the AutoUpdatedVersions.props file yet - we will do it after we set our own version.
        if ( !AutoUpdatedVersionsFile.TryWrite( context, true, out var hasChangesInDependencies, out _, out _, out var currentOrInheritedVersion ) )
        {
            return false;
        }

        if ( !hasChangesInDependencies && !hasChangesSinceLastDeployment )
        {
            console.WriteWarning( $"There are no changes since the last deployment." );

            return true;
        }

        Version? oldVersion;

        if ( product.MainVersionDependency == null )
        {
            // This updates MainVersion.props.
            if ( !product.BumpStrategy.TryBumpVersion( product, context, out oldVersion, out _ ) )
            {
                return false;
            }
        }
        else
        {
            if ( hasChangesSinceLastDeployment && !hasChangesInDependencies )
            {
                const string message =
                    "There are changes in the current repo but no changes in dependencies. However, the current repo does not have its own versioning.";

                if ( settings.Force )
                {
                    console.WriteImportantMessage( $"{message} This is being ignored using --force." );

                    return true;
                }

                console.WriteError( $"{message} Do a fake change in a parent repo or use --force." );

                return false;
            }

            oldVersion = new Version( currentVersion );
        }

        // Now save AutoUpdatedVersions.props.
        if ( !AutoUpdatedVersionsFile.TryWrite( context, false, out _, out _, out _, out var newVersion ) )
        {
            return false;
        }

        // Commit the version bump.
        if ( !GitIntegrationHelper.TryCommitVersionBump( context, oldVersion, new Version( newVersion ) ) )
        {
            return false;
        }

        // If we are running in TeamCity, push.
        if ( context.IsContinuousIntegrationBuild )
        {
            if ( !GitHelper.TryPush( context ) )
            {
                return false;
            }
        }

        return true;
    }
}