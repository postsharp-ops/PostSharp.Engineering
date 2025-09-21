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

    public static bool Execute( BuildContext context, BumpSettings settings )
    {
        var product = context.Product;
        context.Console.WriteHeading( $"Bumping the '{product.ProductName}' version" );

        var developmentBranch = product.DependencyDefinition.Branch;

        if ( context.Branch != developmentBranch )
        {
            context.Console.WriteError(
                $"The version bump can only be executed on the development branch ('{developmentBranch}'). The current branch is '{context.Branch}'." );

            return false;
        }

        // It is forbidden to push to the release branch, but it occasionally happens.
        // We need to make sure that there are no pending changes in the release branch to be merged to the development branch.
        // Failing to do so could result in missing published changes, and it could also break the version bump.
        var releaseBranch = product.DependencyDefinition.ReleaseBranch;

        if ( releaseBranch == null )
        {
            context.Console.WriteMessage( "Skipping check for pending changes from the release branch, as the release branch is not set for this product." );
        }
        else
        {
            context.Console.WriteMessage( $"Checking for pending changes from the release branch ('{releaseBranch}')." );

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
                context.Console.WriteError( $"There are pending changes from the '{releaseBranch}' branch." );
                context.Console.WriteError( $"Check the relevancy of the changes and merge the '{releaseBranch}' branch to the '{developmentBranch}'." );
                context.Console.WriteError( "Failing to do so could result in invalid version number of this product." );

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
            context.Console.WriteWarning( "Version has already been bumped since the last deployment." );

            return true;
        }

        // Read the current version of the dependencies directly from source control.
        if ( !TryReadDependencyVersionsFromSourceRepos( context, true, out var dependencyVersions ) )
        {
            return false;
        }

        // Comparing the actual version of dependencies with the versions stored during the last bump.
        var newBumpInfoFile =
            new BumpInfoFile( dependencyVersions );

        var bumpInfoFilePath = Path.Combine(
            context.RepoDirectory,
            product.BumpInfoFilePath );

        var oldBumpFileContent = File.Exists( bumpInfoFilePath ) ? File.ReadAllText( bumpInfoFilePath ) : "";
        var hasChangesInDependencies = newBumpInfoFile.ToString() != oldBumpFileContent;

        if ( !hasChangesInDependencies && !hasChangesSinceLastDeployment )
        {
            context.Console.WriteWarning( $"There are no changes since the last deployment." );

            return true;
        }

        // If there is a change in dependencies versions, we update BumpInfo.txt with changes.
        if ( hasChangesInDependencies )
        {
            context.Console.WriteMessage(
                $"'{bumpInfoFilePath}' contents are outdated. Overwriting its old content '{oldBumpFileContent}' with new content '{newBumpInfoFile}'." );

            File.WriteAllText( bumpInfoFilePath, newBumpInfoFile.ToString() );
        }

        Version? oldVersion;
        Version? newVersion;

        if ( product.MainVersionDependency == null )
        {
            if ( !product.BumpStrategy.TryBumpVersion( product, context, out oldVersion, out newVersion ) )
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
                    context.Console.WriteImportantMessage( $"{message} This is being ignored using --force." );

                    return true;
                }

                context.Console.WriteError( $"{message} Do a fake change in a parent repo or use --force." );

                return false;
            }

            var oldBumpInfo = BumpInfoFile.Parse( oldBumpFileContent );
            newVersion = dependencyVersions[product.MainVersionDependency.Name];
            oldVersion = oldBumpInfo?.Dependencies[product.MainVersionDependency.Name];
        }

        // Commit the version bump.
        if ( !GitIntegrationHelper.TryCommitVersionBump( context, oldVersion, newVersion, settings ) )
        {
            return false;
        }

        return true;
    }

    private static bool TryReadDependencyVersionsFromSourceRepos(
        BuildContext context,
        bool snapshotDependenciesOnly,
        [NotNullWhen( true )] out Dictionary<string, Version>? dependencyVersions )
    {
        var product = context.Product;
        dependencyVersions = new Dictionary<string, Version>();

        var allDependencies =
            product.ParametrizedDependencies.Select( x => x.Definition )
                .Union( product.SourceDependencies )
                .Union( product.MainVersionDependency == null ? [] : [product.MainVersionDependency] );

        foreach ( var dependency in allDependencies )
        {
            if ( snapshotDependenciesOnly && !dependency.GenerateSnapshotDependency )
            {
                continue;
            }

            var mainVersionFile = $"{dependency.EngineeringDirectory}/MainVersion.props";

            if ( !dependency.VcsRepository.TryDownloadTextFile( context.Console, dependency.Branch, mainVersionFile, out var mainVersionContent ) )
            {
                return false;
            }

            if ( !MainVersionFile.TryParse( context, mainVersionContent, out var mainVersionFileInfo ) )
            {
                return false;
            }

            dependencyVersions.Add( dependency.Name, Version.Parse( mainVersionFileInfo.MainVersion ) );
        }

        return true;
    }
}