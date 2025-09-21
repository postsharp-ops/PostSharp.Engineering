// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Files;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Dependencies;

[PublicAPI]
public class BaseDependenciesCommandSettings : CommonCommandSettings
{
    [Description( "Build configuration (Debug | Release | Public)" )]
    [CommandOption( "-c|--configuration" )]
    [Obsolete( "Use the BuildConfiguration property. " )]
    public BuildConfiguration? BuildConfiguration { get; set; }

    public bool TryGetBuildConfiguration( BuildContext context, out BuildConfiguration configuration )
    {
#pragma warning disable CS0618
        if ( this.BuildConfiguration != null )
        {
            configuration = this.BuildConfiguration.Value;

            return true;
        }
#pragma warning restore CS0618

        var defaultConfiguration = ConfigurationNeutralVersionFile.ReadDefaultConfiguration( context );

        if ( defaultConfiguration == null )
        {
            context.Console.WriteWarning( "There was no current configuration. Choosing the Debug configuration." );

            configuration = Build.BuildConfiguration.Debug;

            return true;
        }

        configuration = defaultConfiguration.Value;

        context.Console.WriteMessage( $"Using the prepared build configuration: {configuration}." );

        return true;
    }
}