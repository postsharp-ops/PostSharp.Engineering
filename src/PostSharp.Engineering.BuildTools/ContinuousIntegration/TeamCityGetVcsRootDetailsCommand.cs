// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

[UsedImplicitly]
public class TeamCityGetVcsRootDetailsCommand : BaseCommand<TeamCityGetVcsRootDetailsCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, TeamCityGetVcsRootDetailsCommandSettings settings )
    {
        if ( !TeamCityHelper.TryConnectTeamCity( context, out var tc ) )
        {
            return false;
        }

        if ( !tc.TryGetVcsRootDetails( context.Console, settings.Id ) )
        {
            return false;
        }

        return true;
    }
}