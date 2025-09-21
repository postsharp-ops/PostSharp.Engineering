// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.Arguments;
using System;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;

public abstract class TeamCityBuildStep
{
    public TeamCityBuildConfigurationParameterBase[]? BuildConfigurationParameters { get; init; }

    public abstract string GenerateTeamCityCode();

    public virtual void InsertPrerequisites( IReadOnlyList<TeamCityBuildStep> previousSteps, Action<TeamCityBuildStep> addStep ) { }
}