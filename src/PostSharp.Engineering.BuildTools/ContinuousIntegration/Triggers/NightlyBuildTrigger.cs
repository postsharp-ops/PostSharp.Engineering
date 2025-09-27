// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Arguments;
using System.IO;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Triggers;

/// <summary>
/// Generates a build trigger that triggers the build daily at 22:00 for the default branch.
/// </summary>
[PublicAPI]
public class NightlyBuildTrigger : IBuildTrigger
{
    public int Hour { get; }

    public int Minute { get; }

    public bool WithPendingChangesOnly { get; }

    public string? BranchFilter { get; init; }

    public BuildConfigurationParameter[]? Parameters { get; init; }

    public NightlyBuildTrigger( int hour, bool withPendingChangesOnly )
    {
        this.Hour = hour;
        this.WithPendingChangesOnly = withPendingChangesOnly;
    }

    public NightlyBuildTrigger( int hour, int minute, bool withPendingChangesOnly )
    {
        this.Hour = hour;
        this.Minute = minute;
        this.WithPendingChangesOnly = withPendingChangesOnly;
    }

    public void GenerateTeamcityCode( TextWriter writer, string? branchFilter = null )
    {
        void WriteIndented( string text )
        {
            writer.Write( "            " );
            writer.WriteLine( text );
        }

        writer.WriteLine( "        schedule {" );

        WriteIndented( "schedulingPolicy = daily {" );
        WriteIndented( $"    hour = {this.Hour}" );
        WriteIndented( $"    minute = {this.Minute}" );
        WriteIndented( "}" );

        branchFilter = this.BranchFilter ?? branchFilter ?? "+:<default>";
        WriteIndented( $"branchFilter = \"{branchFilter}\"" );

        WriteIndented( "triggerBuild = always()" );
        WriteIndented( $"withPendingChangesOnly = {this.WithPendingChangesOnly.ToString().ToLowerInvariant()}" );

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