// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using Spectre.Console.Cli;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.Build.Model;

public abstract class ProductExtension
{
    internal abstract bool AddTeamcityBuildConfiguration( BuildContext context, List<TeamCityBuildConfiguration> teamCityBuildConfigurations );

    internal abstract bool AddCommands( IConfigurator root, BaseCommandData data );
}