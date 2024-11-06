// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Git;

[UsedImplicitly]
public class PrintBranchPoliciesCommand : BaseCommand<BaseBuildSettings>
{
    protected override bool ExecuteCore( BuildContext context, BaseBuildSettings settings )
    {
        context.Console.WriteHeading( "Getting branch policies" );
        
        if ( !GitHelper.TryGetRemoteUrl( context, out var remoteUrl ) )
        {
            return false;
        }

        try
        {
            Task<bool> getBranchPoliciesTask;

            if ( GitHubRepository.TryParse( remoteUrl, out var gitHubRepository ) )
            {
                getBranchPoliciesTask = GitHubHelper.TryPrintBranchPoliciesAsync(
                    context,
                    gitHubRepository );
            }
            else
            {
                context.Console.WriteError( $"Unknown VCS or unexpected repo URL format. Repo URL: '{remoteUrl}'." );

                return false;
            }

            if ( !getBranchPoliciesTask.ConfigureAwait( false ).GetAwaiter().GetResult() )
            {
                return false;
            }
        }
        catch ( Exception e )
        {
            context.Console.WriteError( e.ToString() );

            return false;
        }

        return true;
    }
}