// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Arguments;

public class TextBuildConfigurationParameter : BuildConfigurationParameter
{
    public string Label { get; }

    public bool AllowEmpty { get; init; }

    public string Description { get; init; }

    public (string Regex, string ValidationMessage)? Validation { get; init; }

    public ParameterDisplay Display { get; init; }

    public TextBuildConfigurationParameter( string name, string label, string description, string defaultValue = "", bool allowEmpty = false )
        : base( name, defaultValue )
    {
        this.Label = label;
        this.Description = description;
        this.AllowEmpty = allowEmpty;
    }

    public override string GenerateTeamCityCode()
        => $"""
                    text(
                        "{KotlinHelper.EscapeString( this.Name )}", 
                        "{KotlinHelper.EscapeString( this.Value )}", 
                        label ="{KotlinHelper.EscapeString( this.Label )}",
                        description = "{KotlinHelper.EscapeString( this.Description )}"{(!this.AllowEmpty ? "" : ", allowEmpty = true")}{(!this.Validation.HasValue ? "" : @$", 
                        regex = """"""{KotlinHelper.EscapeString( this.Validation.Value.Regex )}"""""",  
                        validationMessage = ""{KotlinHelper.EscapeString( this.Validation.Value.ValidationMessage )}
                        display = ParameterDisplay.{this.Display.ToString().ToUpperInvariant()}""")})
            """;
}