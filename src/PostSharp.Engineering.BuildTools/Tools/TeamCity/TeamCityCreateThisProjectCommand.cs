// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.BuildTools.Tools.TeamCity;

[UsedImplicitly]
internal class TeamCityCreateThisProjectCommand : BaseCommand<TeamCityCreateThisProjectCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, TeamCityCreateThisProjectCommandSettings settings )
    {
        context.Console.WriteHeading( $"Creating project '{context.Product.DependencyDefinition.Name}'" );

        if ( !TeamCityHelper.TryCreateProject( context ) )
        {
            return false;
        }

        context.Console.WriteSuccess( $"Project '{context.Product.DependencyDefinition.Name}' created successfully." );

        return true;
    }
}