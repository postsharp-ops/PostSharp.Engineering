// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.BuildTools.Tools.TeamCity;

[UsedImplicitly]
internal class TeamCityGetProjectDetailsCommand : BaseCommand<TeamCityGetProjectDetailsCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, TeamCityGetProjectDetailsCommandSettings settings )
    {
        if ( !TeamCityHelper.TryConnectTeamCity( context, out var tc ) )
        {
            return false;
        }

        var success = true;

        context.Console.WriteMessage( "Project:" );

        success &= tc.TryGetProjectDetails( context.Console, settings.Id );

        context.Console.WriteMessage( "Versioned settings configuration:" );

        success &= tc.TryGetProjectVersionedSettingsConfiguration( context.Console, settings.Id );

        return success;
    }
}