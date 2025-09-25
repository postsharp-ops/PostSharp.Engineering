// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using Spectre.Console.Cli;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Text;
using Environment = System.Environment;

#pragma warning disable CA1305

namespace PostSharp.Engineering.BuildTools.Build
{
    /// <summary>
    /// Settings for <see cref="BuildCommand"/>, <see cref="PrepareCommand"/> and <see cref="CleanCommand"/>.
    /// </summary>
    [PublicAPI]
    public class BuildSettings : BaseBuildSettings
    {
        private int? _solutionId;

        protected override void AppendSettings( StringBuilder stringBuilder )
        {
            base.AppendSettings( stringBuilder );

            if ( this.BuildNumber != null )
            {
                stringBuilder.Append( $"--buildNumber {this.BuildNumber} " );
            }

            if ( this.BuildType != null )
            {
                stringBuilder.Append( $"--buildType {this.BuildType} " );
            }

            if ( this.Verbosity != Verbosity.Minimal )
            {
                stringBuilder.Append( $"-v {this.Verbosity} " );
            }

            if ( this.NoDependencies )
            {
                stringBuilder.Append( "--no-dependencies " );
            }

            if ( this.IncludeTests )
            {
                stringBuilder.Append( "--include-tests " );
            }

            if ( this.NoConcurrency )
            {
                stringBuilder.Append( "--no-concurrency " );
            }

            if ( this.SolutionId != null )
            {
                stringBuilder.Append( $"--solution {this.SolutionId} " );
            }

            if ( this.Recursive )
            {
                stringBuilder.Append( "--recursive " );
            }

            if ( this.DateTag != null )
            {
                stringBuilder.Append( $"--if-older {this.DateTag} " );
            }

            if ( this.NoSign )
            {
                stringBuilder.Append( "--no-sign " );
            }

            if ( this.CreateZip )
            {
                stringBuilder.Append( "--zip " );
            }

            if ( this.CreateConsolidatedDirectory )
            {
                stringBuilder.Append( "--consolidated " );
            }

            if ( this.AnalyzeCoverage )
            {
                stringBuilder.Append( "--analyze-coverage " );
            }

            if ( !string.IsNullOrEmpty( this.UserName ) )
            {
                stringBuilder.Append( $"--user {this.UserName}" );
            }
        }

        [Description( "Creates a numbered build (typically for an internal CI build). This option is ignored when the build configuration is 'Public'." )]
        [CommandOption( "--buildNumber" )]
        public int? BuildNumber { get; set; }

        [Description( "Specifies the name of the build type in TeamCity." )]
        [CommandOption( "--buildType" )]
        public string? BuildType { get; set; }

        [Description( "Sets the verbosity" )]
        [CommandOption( "-v|--verbosity" )]
        [DefaultValue( Verbosity.Minimal )]
        public Verbosity Verbosity { get; set; }

        [Description( "Executes only the current command, but not the previous command" )]
        [CommandOption( "--no-dependencies" )]
        public bool NoDependencies { get; set; }

        [Description( "Determines whether test-only assemblies should be included in the operation" )]
        [CommandOption( "--include-tests" )]
        public bool IncludeTests { get; set; }

        [Description( "Disables concurrent processing" )]
        [CommandOption( "--no-concurrency" )]
        public bool NoConcurrency { get; set; }

        [Description( "Passed the --disable-build-servers option to dotnet build." )]
        [CommandOption( "--disable-build-servers" )]
        public bool DisableBuildServers { get; set; }

        [Description(
            "Specifies the solution that should be built. The list of solutions and their id is given by the `list-solutions` command." +
            "Setting this property automatically adds the --no-dependencies option." )]
        [CommandOption( "--solution" )]
        public int? SolutionId
        {
            get => this._solutionId;
            set
            {
                this._solutionId = value;
                this.NoDependencies = true;
            }
        }

        [Description( "Recursively builds any local dependency before building the current repo." )]
        [CommandOption( "--recursive" )]
        public bool Recursive { get; set; }

        [Description(
            "Does not build if there is already a build newer than the indicated date. This setting is used internally to implement --recursive, and should not be used manually." )]
        [CommandOption( "--if-older", IsHidden = true )]
        public long? DateTag { get; set; }

        [Description( "Does not sign the assemblies and packages" )]
        [CommandOption( "--no-sign" )]
        public bool NoSign { get; set; }

        [Description( "Creates a zip file with all artifacts" )]
        [CommandOption( "--zip" )]
        public bool CreateZip { get; set; }

        [Description( "Analyzes the test coverage while and after running the tests" )]
        [CommandOption( "--analyze-coverage" )]
        public bool AnalyzeCoverage { get; set; }

        // The following option is used e.g. when testing the LinqPad driver. It is not included by default because of 
        // performance of the normal build scenario.

        [Description( "Creates a directory with all packages of the current repo and all transitive dependencies." )]
        [CommandOption( "--consolidated" )]
        public bool CreateConsolidatedDirectory { get; set; }

        [Description(
            "An expression to filter the executed tests as specified at https://learn.microsoft.com/en-us/dotnet/core/testing/selective-unit-tests." )]
        [CommandOption( "--tests-filter" )]
        public string? TestsFilter { get; set; }

        [Description( "Does not clean the NuGet cache. By default, the cache is not cleaned locally, at it is cleaned on a build agent." )]
        [CommandOption( "--no-nuget-cache-cleanup" )]
        public bool NoNuGetCacheCleanup { get; set; }

        [Description( "Overrides the user name." )]
        [CommandOption( "--user" )]
        public string UserName { get; set; } = Environment.GetEnvironmentVariable( EnvironmentVariableNames.EngUserName ) ?? Environment.UserName;

        public BuildSettings WithIncludeTests( bool value )
        {
            var clone = (BuildSettings) this.MemberwiseClone();
            clone.IncludeTests = value;

            return clone;
        }

        public BuildSettings WithoutConcurrency()
        {
            var clone = (BuildSettings) this.MemberwiseClone();
            clone.NoConcurrency = true;

            return clone;
        }

        public BuildSettings WithoutLogo()
        {
            var clone = (BuildSettings) this.MemberwiseClone();
            clone.NoLogo = true;

            return clone;
        }

        public BuildSettings WithAdditionalProperties( ImmutableDictionary<string, string> properties )
        {
            if ( properties.IsEmpty )
            {
                return this;
            }

            var clone = (BuildSettings) this.MemberwiseClone();
            clone.Properties = clone.Properties.AddRange( properties );

            return clone;
        }

        public BuildSettings WithTestsFilter( string testsFilter )
        {
            var clone = (BuildSettings) this.MemberwiseClone();
            clone.TestsFilter = testsFilter;

            return clone;
        }

        public VersionSpec GetVersionSpec( BuildConfiguration configuration )
            => configuration == BuildConfiguration.Public
                ? new VersionSpec( VersionKind.Public )
                : this.BuildNumber != null
                    ? new VersionSpec( VersionKind.Numbered, this.BuildNumber.Value )
                    : new VersionSpec( VersionKind.Local );
    }
}