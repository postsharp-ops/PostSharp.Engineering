// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.Arguments;

public class TeamCityTextBuildConfigurationParameter : TeamCityBuildConfigurationParameter
{
    public string Label { get; }

    public bool AllowEmpty { get; init; }

    public string Description { get; init; }

    public (string Regex, string ValidationMessage)? Validation { get; init; }

    public TeamCityTextBuildConfigurationParameter( string name, string label, string description, string defaultValue = "", bool allowEmpty = false )
        : base( name, defaultValue )
    {
        this.Label = label;
        this.Description = description;
        this.AllowEmpty = allowEmpty;
    }

    public override string GenerateTeamCityCode()
        => @$"        text(""{this.Name}"", ""{this.Value}"", label = ""{this.Label}"", description = ""{this.Description}""{(!this.AllowEmpty ? "" : ", allowEmpty = true")}{(!this.Validation.HasValue ? "" : @$", regex = """"""{this.Validation.Value.Regex}"""""", validationMessage = ""{this.Validation.Value.ValidationMessage}""")})";
}