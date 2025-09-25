// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace PostSharp.Engineering.BuildTools
{
    /// <summary>
    /// Declares the options that are common to all settings.
    /// </summary>
    [PublicAPI]
    public class CommonCommandSettings : CommandSettings
    {
        private string[] _unparsedProperties = [];

        protected virtual void AppendSettings( StringBuilder stringBuilder )
        {
            if ( this.NoLogo )
            {
                stringBuilder.Append( "--nologo " );
            }

            if ( this.Verbose )
            {
                stringBuilder.Append( "--verbose " );
            }

            if ( this.Force )
            {
                stringBuilder.Append( "--force " );
            }

            if ( this.SimulateContinuousIntegration )
            {
                stringBuilder.Append( "--ci " );
            }

            if ( this.Timeout != null )
            {
                stringBuilder.Append( $"--timeout {this.Timeout} " );
            }
        }

        public override string ToString()
        {
            var stringBuilder = new StringBuilder();
            this.AppendSettings( stringBuilder );

            return stringBuilder.ToString().Trim();
        }

        [Description( "Do not display the logo" )]
        [CommandOption( "--nologo" )]
        public bool NoLogo { get; protected set; }

        [Description( "Makes the engineering tool verbose (but not msbuild or dotnet)." )]
        [CommandOption( "--verbose" )]
        public bool Verbose { get; protected set; }

        [Description( "Use force" )]
        [CommandOption( "--force" )]
        public bool Force { get; init; }

        [Description( "Lists the additional properties supported by the command" )]
        [CommandOption( "--list-properties" )]
        public bool ListProperties { get; protected set; }

        [Description( "Attach the debugger to the process before executing the command" )]
        [CommandOption( "--debug" )]
        public bool Debug { get; protected set; }

        [Description( "Simulate a continuous integration build by setting the build ContinuousIntegrationBuild property to TRUE." )]
        [CommandOption( "--ci" )]
        public bool SimulateContinuousIntegration { get; set; }

        [Description( "Use the project or solution directory as a working directory." )]
        [CommandOption( "--project-dir-as-working-dir" )]
        public bool UseProjectDirectoryAsWorkingDirectory { get; set; }

        [Description( "Use local dependencies instead of build server dependencies." )]
        [CommandOption( "--use-local-dependencies" )]
        public bool UseLocalDependencies { get; set; }

        [Description( "Properties in form Name=Value" )]
        [CommandOption( "-p|--property" )]
        public string[] UnparsedProperties
        {
            get => this._unparsedProperties;

            protected set
            {
                this._unparsedProperties = value;

                this.Properties = this.Properties.AddRange(
                    value.Select( v =>
                    {
                        var split = v.Split( '=' );

                        if ( split.Length > 1 )
                        {
                            return new KeyValuePair<string, string>( split[0].Trim(), split[1].Trim() );
                        }
                        else
                        {
                            return new KeyValuePair<string, string>( split[0].Trim(), "True" );
                        }
                    } ) );
            }
        }

        [Description( "Overrides the build timeout." )]
        [CommandOption( "--timeout" )]
        public int? Timeout { get; set; }

        public ImmutableDictionary<string, string> Properties { get; protected set; } =
            ImmutableDictionary.Create<string, string>( StringComparer.OrdinalIgnoreCase );

        public virtual void Initialize( BuildContext context ) { }
    }
}