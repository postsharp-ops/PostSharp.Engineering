// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.Arguments;

public class TeamCityBuildConfigurationParameter
{
    public string Name { get; }

    public string Value { get; }

    public TeamCityBuildConfigurationParameter( string name, string value )
    {
        this.Name = name;
        this.Value = value;
    }

    public virtual string GenerateTeamCityCode() => @$"        param(""{this.Name}"", ""{this.Value}"")";
}