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
        context.Console.WriteHeading( "Setting default branch" );

        if ( !GitHelper.TryGetRemoteUrl( context, out var remoteUrl ) )
        {
            return false;
        }

        try
        {
            Task<bool> setBranchPoliciesTask;

            if ( AzureDevOpsRepository.TryParse( remoteUrl, out var azureDevOpsRepository ) )
            {
                var defaultBranch = settings.DefaultBranch;

                // Implicitly, we set the default branch to the downstream version, as this should be the development one,
                // because on Azure DevOps, most pull requests usually go to the development version.
                if ( defaultBranch == null )
                {
                    var defaultProductFamily = context.Product.ProductFamily.DownstreamProductFamily;

                    if ( defaultProductFamily == null )
                    {
                        context.Console.WriteError( "Default branch was not given and cannot be determined." );

                        return false;
                    }

                    if ( !defaultProductFamily.TryGetDependencyDefinition( context.Product.DependencyDefinition.Name, out var defaultDependencyDefinition ) )
                    {
                        return false;
                    }

                    defaultBranch = defaultDependencyDefinition.Branch;
                }
                
                setBranchPoliciesTask = AzureDevOpsHelper.TrySetDefaultBranchAsync(
                    context.Console,
                    azureDevOpsRepository,
                    defaultBranch,
                    settings.Dry );
            }
            else if ( GitHubRepository.TryParse( remoteUrl, out var gitHubRepository ) )
            {
                // Implicitly, we set the default branch to the current release branch,
                // as this is what user should get by default when approaching a public GitHub repository.
                var defaultBranch = settings.DefaultBranch ?? context.Product.DependencyDefinition.ReleaseBranch;

                if ( defaultBranch == null )
                {
                    context.Console.WriteError( "Default branch was not given and cannot be determined." );

                    return false;
                }
                
                setBranchPoliciesTask = GitHubHelper.TrySetDefaultBranchAsync(
                    context.Console,
                    gitHubRepository,
                    defaultBranch,
                    settings.Dry );
            }
            else
            {
                context.Console.WriteError( $"Unknown VCS or unexpected repo URL format. Repo URL: '{remoteUrl}'." );

                return false;
            }

            if ( !setBranchPoliciesTask.ConfigureAwait( false ).GetAwaiter().GetResult() )
            {
                return false;
            }
        }
        catch ( Exception e )
        {
            context.Console.WriteError( e.ToString() );

            return false;
        }

        context.Console.WriteSuccess( settings.Dry ? "Dry run of default branch setting succeeded." : "Default branch set successfully." );

        return true;
    }
}