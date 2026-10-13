// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Tools.XmlDoc;

[PublicAPI]
public class RemoveInternalsCommandSettings : CommonCommandSettings
{
    private string[] _unparsedMSBuildProperties = [];

    [Description( "Path to the xml file. The dll is assumed to be next to it." )]
    [CommandArgument( 0, "<xml-path>" )]
    public string XmlPath { get; init; } = null!;

    [Description( "Path to the csproj file." )]
    [CommandArgument( 1, "<project-path>" )]
    public string ProjectPath { get; init; } = null!;

    [Description( "Does not save the file. Use with --verbose." )]
    [CommandOption( "--dry" )]
    public bool Dry { get; init; }

    [Description(
        "MSBuild global properties in form Name=Value, used to load the project. A multi-targeted project must be loaded with the "
        + "TargetFramework property of the documentation file, otherwise it is loaded for its first target framework." )]
    [CommandOption( "--msbuild-property" )]
    public string[] UnparsedMSBuildProperties
    {
        get => this._unparsedMSBuildProperties;

        init
        {
            this._unparsedMSBuildProperties = value;

            this.MSBuildProperties = this.MSBuildProperties.SetItems(
                value.Select( v =>
                {
                    var split = v.Split( '=', 2 );

                    return split.Length > 1
                        ? new KeyValuePair<string, string>( split[0].Trim(), split[1].Trim() )
                        : new KeyValuePair<string, string>( split[0].Trim(), "True" );
                } ) );
        }
    }

    public ImmutableDictionary<string, string> MSBuildProperties { get; private set; } =
        ImmutableDictionary.Create<string, string>( StringComparer.OrdinalIgnoreCase );
}