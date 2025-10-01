// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Arguments;
using System;
using System.IO;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Triggers;

/// <summary>
/// Generates a build trigger that triggers the build when the source code has changed in the default branch.
/// </summary>
[PublicAPI]
public class SourceBuildTrigger : IBuildTrigger
{
    public bool WatchChangesInDependencies { get; init; } = true;

    public string? BranchFilter { get; init; }

    public BuildConfigurationParameter[]? Parameters { get; init; }
    
    public TimeSpan? QuietPeriod { get; init; } = TimeSpan.FromHours( 2 );

    public void GenerateTeamcityCode( TextWriter writer, string? branchFilter = null )
    {
        void WriteIndented( string text )
        {
            writer.Write( "            " );
            writer.WriteLine( text );
        }

        writer.WriteLine( "        vcs {" );

        WriteIndented( $"watchChangesInDependencies = {this.WatchChangesInDependencies.ToString().ToLowerInvariant()}" );

        branchFilter = this.BranchFilter ?? branchFilter ?? "+:<default>";
        WriteIndented( $"branchFilter = \"{branchFilter}\"" );

        if ( this.QuietPeriod != null )
        {
            WriteIndented( " quietPeriodMode = VcsTrigger.QuietPeriodMode.USE_CUSTOM" );
            WriteIndented( $" quietPeriod = {this.QuietPeriod.Value.TotalSeconds}" );
        }

        WriteIndented( "// Build will not trigger automatically if the commit message contains comment value." );
        WriteIndented( "triggerRules = \"-:comment=<<VERSION_BUMP>>|<<DEPENDENCIES_UPDATED>>:**\"" );

        if ( this.Parameters != null )
        {
            WriteIndented( "buildParams {" );

            foreach ( var parameter in this.Parameters )
            {
                writer.Write( "        " );
                writer.WriteLine( parameter.GenerateTeamCityCode() );
            }

            WriteIndented( "}" );
        }

        writer.WriteLine( "        }" );
    }
}
