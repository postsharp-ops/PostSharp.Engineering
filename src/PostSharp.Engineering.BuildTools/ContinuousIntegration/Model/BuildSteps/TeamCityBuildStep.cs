// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.Arguments;
using System;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;

public abstract class TeamCityBuildStep
{
    private readonly List<TeamCityBuildConfigurationParameter> _parameters = new();

    public IReadOnlyList<TeamCityBuildConfigurationParameter> BuildConfigurationParameters => this._parameters;

    protected void AddParameter( TeamCityBuildConfigurationParameter parameter ) => this._parameters.Add( parameter );

    public abstract string GenerateTeamCityCode();

    public virtual void InsertPrerequisites( IReadOnlyList<TeamCityBuildStep> previousSteps, Action<TeamCityBuildStep> addStep ) { }
}