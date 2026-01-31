// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Tools.Git;

[UsedImplicitly]
internal class SetDefaultBranchCommand : BaseCommand<SetDefaultBranchSettings>
{
    protected override bool ExecuteCore( BuildContext context, SetDefaultBranchSettings settings )
    {
        var console = context.Console;
        var product = context.Product;

        console.WriteHeading( "Setting default branch" );

        if ( !GitHelper.TryGetRemoteUrl( context, out var remoteUrl ) )
        {
            return false;
        }

        try
        {
            Task<bool> setBranchPoliciesTask;

            if ( AzureDevOpsRepository.TryParse( remoteUrl, out var azureDevOpsRepository ) )
            {
                // For Azure DevOps, the default branch must be specified explicitly or defaults to the current branch.
                var defaultBranch = settings.DefaultBranch ?? product.DependencyDefinition.Branch;

                setBranchPoliciesTask = AzureDevOpsHelper.TrySetDefaultBranchAsync(
                    context,
                    azureDevOpsRepository,
                    defaultBranch,
                    settings.Dry );
            }
            else if ( GitHubRepository.TryParse( remoteUrl, out var gitHubRepository ) )
            {
                // Implicitly, we set the default branch to the current release branch,
                // as this is what user should get by default when approaching a public GitHub repository.
                var defaultBranch = settings.DefaultBranch ?? product.DependencyDefinition.ReleaseBranch;

                if ( defaultBranch == null )
                {
                    console.WriteError( "Default branch was not given and cannot be determined." );

                    return false;
                }

                setBranchPoliciesTask = GitHubHelper.TrySetDefaultBranchAsync(
                    console,
                    gitHubRepository,
                    defaultBranch,
                    settings.Dry );
            }
            else
            {
                console.WriteError( $"Unknown VCS or unexpected repo URL format. Repo URL: '{remoteUrl}'." );

                return false;
            }

            if ( !setBranchPoliciesTask.ConfigureAwait( false ).GetAwaiter().GetResult() )
            {
                return false;
            }
        }
        catch ( Exception e )
        {
            console.WriteError( e.ToString() );

            return false;
        }

        console.WriteSuccess( settings.Dry ? "Dry run of default branch setting succeeded." : "Default branch set successfully." );

        return true;
    }
}