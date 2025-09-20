// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.Text;

#pragma warning disable CA1305

namespace PostSharp.Engineering.BuildTools.Build;

/// <summary>
/// Base for <see cref="BuildSettings"/> and <see cref="PublishSettings"/>. Defines a <see cref="BuildConfiguration"/>
/// option that resolves to the configuration of the latest build if any was define, otherwise to Debug.
/// </summary>
[PublicAPI]
public class BaseBuildSettings : CommonCommandSettings
{
    private BuildConfiguration? _resolvedConfiguration;

    [Description( "Sets the build configuration (Debug | Release | Public)" )]
    [CommandOption( "-c|--configuration" )]
    public BuildConfiguration BuildConfiguration
    {
        get
            => this._resolvedConfiguration
               ?? throw new InvalidOperationException( "Call the Initialize method or set the BuildConfiguration first ." );
        set => this._resolvedConfiguration = value;
    }

    [Description( "Overrides the .NET SDK version." )]
    [CommandOption( "--sdk-version" )]
    public string? SdkVersion { get; init; }

    protected override void AppendSettings( StringBuilder stringBuilder )
    {
        base.AppendSettings( stringBuilder );

        if ( this._resolvedConfiguration != null )
        {
            stringBuilder.Append( $"-c {this._resolvedConfiguration} " );
        }

        if ( !string.IsNullOrEmpty( this.SdkVersion ) )
        {
            stringBuilder.Append( $"--sdk-version {this.SdkVersion} " );
        }
    }

    public override void Initialize( BuildContext context )
    {
        if ( this._resolvedConfiguration != null )
        {
            return;
        }

        var defaultConfiguration = context.Product.ReadDefaultConfiguration( context );

        if ( defaultConfiguration == null )
        {
            context.Console.WriteMessage( $"Using the default configuration Debug." );

            this._resolvedConfiguration = BuildConfiguration.Debug;
        }
        else
        {
            context.Console.WriteMessage( $"Using the prepared build configuration: {defaultConfiguration.Value}." );

            this._resolvedConfiguration = defaultConfiguration.Value;
        }
    }
}