// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Arguments;
using PostSharp.Engineering.BuildTools.Docker;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;

internal abstract class BuildStep
{
    private readonly DockerSpec? _dockerSpec;

    private readonly List<BuildConfigurationParameter> _parameters = new();

    protected BuildStep( DockerSpec? dockerSpec )
    {
        this._dockerSpec = dockerSpec;
    }

    public IReadOnlyList<BuildConfigurationParameter> BuildConfigurationParameters => this._parameters;

    protected void AddParameter( BuildConfigurationParameter parameter ) => this._parameters.Add( parameter );

    public abstract string GenerateTeamCityCode();

    public void InsertPrerequisites( IReadOnlyList<BuildStep> previousSteps, Action<BuildStep> addStep )
    {
        if ( this._dockerSpec != null )
        {
            var prepareImageStep = previousSteps
                .OfType<EngineeringPrepareImageBuildStep>()
                .SingleOrDefault( i => i.DockerSpec.ImageName == this._dockerSpec.ImageName );

            if ( prepareImageStep == null )
            {
                addStep( new EngineeringPrepareImageBuildStep( "PrepareImage", this._dockerSpec ) );
            }
        }
    }

    /// <summary>
    /// Gets a time that should be added to the complete build configuration timeout.
    /// </summary>
    public virtual TimeSpan AdditionalTimeout { get; init; } = TimeSpan.Zero;
}