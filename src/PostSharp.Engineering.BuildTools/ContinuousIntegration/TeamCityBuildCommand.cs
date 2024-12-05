// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

[UsedImplicitly]
public class TeamCityBuildCommand : BaseCommand<TeamCityBuildCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, TeamCityBuildCommandSettings settings )
        => TeamCityHelper.TriggerTeamCityBuild(
            context,
            settings,
            settings.ProductFamilyName,
            settings.ProductFamilyVersion,
            settings.ProductName,
            settings.TeamCityBuildType,
            settings.BuildConfiguration );
}