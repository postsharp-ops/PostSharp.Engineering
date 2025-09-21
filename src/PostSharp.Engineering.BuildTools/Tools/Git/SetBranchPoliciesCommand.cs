// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Tools.Git;

[UsedImplicitly]
internal class SetBranchPoliciesCommand : BaseCommand<SetBranchPoliciesSettings>
{
    protected override bool ExecuteCore( BuildContext context, SetBranchPoliciesSettings settings )
    {
        context.Console.WriteHeading( "Setting branch policies" );

        if ( !GitHelper.TryGetRemoteUrl( context, out var remoteUrl ) )
        {
            return false;
        }

        var buildStatusGenre = "TeamCity";
        var buildStatusName = context.Product.DependencyDefinition.CiConfiguration.PullRequestStatusCheckBuildType;

        try
        {
            Task<bool> setBranchPoliciesTask;

            if ( VcsUrlParser.TryGetRepository( remoteUrl, out var repository ) )
            {
                setBranchPoliciesTask = repository.TrySetBranchPoliciesAsync(
                    context,
                    buildStatusGenre,
                    buildStatusName,
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

        context.Console.WriteSuccess( settings.Dry ? "Dry run of branch policies setting succeeded." : "Branch policies set successfully." );

        return true;
    }
}