// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.Arguments;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model
{
    internal class TeamCityBuildConfiguration
    {
        public string ObjectName { get; }

        public string Name { get; }
        
        public string DefaultBranch { get; }
        
        public string DefaultBranchParameter { get; }
        
        public string VcsRootId { get; }

        public BuildAgentRequirements? BuildAgentRequirements { get; }

        public TeamCityBuildStep[]? BuildSteps { get; init; }

        public bool IsDeployment { get; init; }

        public bool IsComposite => this.BuildAgentRequirements == null;

        public bool IsSshAgentRequired { get; init; }

        public string? ArtifactRules { get; init; }

        public string[]? AdditionalArtifactRules { get; init; }

        public IBuildTrigger[]? BuildTriggers { get; init; }

        public TeamCitySnapshotDependency[]? SnapshotDependencies { get; init; }

        public TeamCitySourceDependency[]? SourceDependencies { get; init; }

        public bool IsDefaultVcsRootUsed { get; init; } = true;

        public TimeSpan? BuildTimeOutThreshold { get; init; }
        
        public TeamCityBuildConfigurationParameterBase[]? Parameters { get; init; }

        public TeamCityBuildConfiguration( string objectName, string name, string defaultBranch, string defaultBranchParameter, string vcsRootId, BuildAgentRequirements? buildAgentRequirements = null )
        {
            this.ObjectName = objectName;
            this.Name = name;
            this.DefaultBranch = defaultBranch;
            this.DefaultBranchParameter = defaultBranchParameter;
            this.VcsRootId = vcsRootId;
            this.BuildAgentRequirements = buildAgentRequirements;
        }

        public void GenerateTeamcityCode( TextWriter writer )
        {
            writer.WriteLine(
                $@"object {this.ObjectName} : BuildType({{

    name = ""{this.Name}""
" );

            if ( this.IsDeployment )
            {
                writer.WriteLine( "    type = Type.DEPLOYMENT" );
                writer.WriteLine();
            }
            else if ( this.IsComposite )
            {
                writer.WriteLine( "    type = Type.COMPOSITE" );
                writer.WriteLine();
            }

            if ( this.ArtifactRules != null )
            {
                if ( this.AdditionalArtifactRules != null )
                {
                    writer.WriteLine(
                        $@"    artifactRules = ""{this.ArtifactRules}\n{string.Join( $@"\n", this.AdditionalArtifactRules.OrderBy( x => x, StringComparer.InvariantCulture ) )}""" );
                }
                else
                {
                    writer.WriteLine( $@"    artifactRules = ""{this.ArtifactRules}""" );
                }

                writer.WriteLine();
            }

            var hasBuildSteps = this.BuildSteps is { Length: > 0 };

            var buildParameters = new List<TeamCityBuildConfigurationParameterBase>();

            if ( hasBuildSteps )
            {
                buildParameters.AddRange(
                    this.BuildSteps!.SelectMany( s => s.BuildConfigurationParameters ?? Enumerable.Empty<TeamCityBuildConfigurationParameterBase>() ) );
            }

            buildParameters.Add(
                new TeamCityTextBuildConfigurationParameterBase(
                    this.DefaultBranchParameter,
                    "Default Branch",
                    "The default branch of this build configuration.",
                    this.DefaultBranch ) );

            if ( this.BuildTimeOutThreshold.HasValue )
            {
                var timeOutParameter = new TeamCityTextBuildConfigurationParameterBase(
                    "TimeOut",
                    "Time-Out",
                    "Timeout, in minutes.",
                    $"{(int) this.BuildTimeOutThreshold.Value.TotalMinutes}" ) { Validation = (@"\d+", "The timeout has to be an integer number.") };

                buildParameters.Add( timeOutParameter );
            }

            if ( this.Parameters != null )
            {
                buildParameters.AddRange( this.Parameters );
            }

            if ( buildParameters.Count > 0 )
            {
                writer.WriteLine(
                    $@"    params {{
{string.Join( Environment.NewLine, buildParameters.Select( p => p.GenerateTeamCityCode() ) )}
    }}
" );
            }

            writer.WriteLine( "    vcs {" );

            if ( this.IsDefaultVcsRootUsed )
            {
                // We set the VCS root explicitly for consolidated as well builds to enable the DefaultBranch paramater.
                writer.WriteLine( @$"        root(AbsoluteId(""{this.VcsRootId}""))" );

                if ( !hasBuildSteps )
                {
                    writer.WriteLine( $"        showDependenciesChanges = true" );
                }
            }

            // Source dependencies.
            var hasSourceDependencies = this.SourceDependencies is { Length: > 0 };

            if ( hasSourceDependencies )
            {
                foreach ( var sourceDependency in this.SourceDependencies! )
                {
                    var objectName = sourceDependency.IsAbsoluteId ? @$"AbsoluteId(""{sourceDependency.ObjectId}"")" : sourceDependency.ObjectId;

                    writer.WriteLine( $@"        root({objectName}, ""{sourceDependency.ArtifactRules}"")" );
                }
            }

            writer.WriteLine( $@"    }}" );

            // Build steps.
            if ( hasBuildSteps )
            {
                if ( this.IsComposite )
                {
                    throw new InvalidOperationException( "Composite build cannot have build steps. Check if the build agent type is set." );
                }

                writer.WriteLine(
                    $@"
    steps {{" );

                foreach ( var buildStep in this.BuildSteps! )
                {
                    writer.WriteLine( buildStep.GenerateTeamCityCode() );
                }

                writer.WriteLine( @"    }" );
            }

            if ( this.BuildTimeOutThreshold.HasValue )
            {
                writer.WriteLine(
                    $$"""
                          failureConditions {
                               executionTimeoutMin = {{(int) this.BuildTimeOutThreshold.Value.TotalMinutes}}
                          }
                      """ );
            }

            if ( !this.IsComposite && this.BuildAgentRequirements != null )
            {
                writer.WriteLine();
                writer.WriteLine( "    requirements {" );

                foreach ( var environmentVariable in this.BuildAgentRequirements.Items )
                {
                    writer.WriteLine( $"        equals(\"{environmentVariable.Name}\", \"{environmentVariable.Value}\")" );
                }

                writer.WriteLine( "    }" );
            }

            var hasSwabra = hasBuildSteps;
            var hasSshAgent = this.IsSshAgentRequired;
            var hasFeatures = hasSwabra || hasSshAgent;

            // Features.
            if ( hasFeatures )
            {
                writer.WriteLine(
                    $@"
    features {{" );

                if ( hasSwabra )
                {
                    writer.WriteLine(
                        $@"        swabra {{
            lockingProcesses = Swabra.LockingProcessPolicy.KILL
            verbose = true
        }}" );
                }

                if ( hasSshAgent )
                {
                    writer.WriteLine(
                        $@"        sshAgent {{
            // By convention, the SSH key name is always PostSharp.Engineering for all repositories using SSH to connect.
            teamcitySshKey = ""PostSharp.Engineering""
        }}" );
                }

                writer.WriteLine( $@"    }}" );
            }

            // Triggers.
            if ( this.BuildTriggers is { Length: > 0 } )
            {
                writer.WriteLine(
                    @"
    triggers {" );

                foreach ( var trigger in this.BuildTriggers )
                {
                    trigger.GenerateTeamcityCode( writer, $"+:{this.DefaultBranch}" );
                }

                writer.WriteLine( @"    }" );
            }

            // Dependencies
            var hasSnapshotDependencies = this.SnapshotDependencies is { Length: > 0 };

            if ( hasSnapshotDependencies )
            {
                writer.WriteLine(
                    $@"
    dependencies {{" );

                foreach ( var dependency in this.SnapshotDependencies! )
                {
                    var objectName = dependency.IsAbsoluteId ? @$"AbsoluteId(""{dependency.ObjectId}"")" : dependency.ObjectId;

                    writer.WriteLine(
                        $@"        dependency({objectName}) {{
            snapshot {{
                     onDependencyFailure = FailureAction.FAIL_TO_START
            }}" );

                    if ( dependency.ArtifactRules != null )
                    {
                        writer.WriteLine(
                            $@"
            artifacts {{
                cleanDestination = true
                artifactRules = ""{dependency.ArtifactRules}""
            }}" );
                    }

                    writer.WriteLine( $@"        }}" );
                }

                writer.WriteLine(
                    $@"     }}" );
            }

            writer.WriteLine(
                $@"
}})" );
        }
    }
}