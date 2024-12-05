// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

[UsedImplicitly]
public class TeamCityCreateThisVcsRootCommand : BaseCommand<TeamCityCreateThisVcsRootCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, TeamCityCreateThisVcsRootCommandSettings settings )
    {
        context.Console.WriteHeading( $"Creating VCS root of project '{context.Product.DependencyDefinition.Name}'" );

        if ( !TeamCityHelper.TryCreateVcsRoot( context, settings.ProjectId ) )
        {
            return false;
        }

        context.Console.WriteSuccess( $"VCS root of project '{context.Product.DependencyDefinition.Name}' created successfully." );

        return true;
    }
}