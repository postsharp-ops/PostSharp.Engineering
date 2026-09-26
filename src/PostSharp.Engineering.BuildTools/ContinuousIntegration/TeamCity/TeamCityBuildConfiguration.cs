// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Arguments;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Triggers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity
{
    internal record TeamCityBuildConfiguration
    {
        public string ObjectName { get; init; }

        public string Name { get; init; }

        public string DefaultBranch { get; init; }

        public string VcsId { get; init; }

        public BuildAgentRequirements? BuildAgentRequirements { get; init; }

        public BuildStep[]? BuildSteps { get; init; }

        public bool IsDeployment { get; init; }

        public bool IsComposite => this.BuildAgentRequirements == null;

        public bool IsSshAgentRequired { get; init; }

        /// <summary>
        /// Gets the name of the TeamCity-uploaded SSH key loaded by the <c>SSH Agent</c> build feature when
        /// <see cref="IsSshAgentRequired"/> is <c>true</c>. When <c>null</c>, the conventional key name
        /// <c>PostSharp.Engineering</c> is used.
        /// </summary>
        public string? SshAgentKeyName { get; init; }

        public string? ArtifactRules { get; init; }

        /// <summary>
        /// Gets the wall-clock limit of the build in minutes, or <c>null</c> for no limit. Without one a build that
        /// hangs holds its agent indefinitely; with one that is too short a legitimately long build is killed, so
        /// the value belongs to the product rather than to a default here.
        /// </summary>
        public int? TimeoutInMinutes { get; init; }

        public string[]? AdditionalArtifactRules { get; init; }

        public IBuildTrigger[]? BuildTriggers { get; init; }

        public TeamCitySnapshotDependency[]? SnapshotDependencies { get; init; }

        public TeamCitySourceDependency[]? SourceDependencies { get; init; }

        public bool IsDefaultVcsRootUsed { get; init; } = true;

        public BuildConfigurationParameter[]? Parameters { get; init; }

        public bool RequiresCommitStatusPublisher { get; init; }

        /// <summary>
        /// Gets or sets the settings of the build features that issue GitHub App installation tokens for the duration of
        /// the build and expose them as environment variables. Empty when the repository is not hosted on GitHub, or
        /// when its product family has no GitHub App connection.
        /// </summary>
        /// <remarks>
        /// There is one entry per GitHub organization the build writes to. The organization of the repository comes
        /// first, and its token lands in <c>GITHUB_TOKEN</c>; every other organization the build checks out a source
        /// dependency from adds an entry whose token lands in <c>GITHUB_TOKEN_&lt;OWNER&gt;</c>. A token belongs to one
        /// GitHub App installation and an installation to one account, so one token cannot serve two organizations.
        /// </remarks>
        public ImmutableArray<GitHubAppBuildScopedTokenSettings> GitHubAppBuildScopedTokens { get; set; } = [];

        /// <summary>
        /// Gets or sets the connection and parameter that replace the ones this build configuration would inherit from
        /// its repository. <c>null</c> for all but the few build configurations that run under an identity of their own.
        /// </summary>
        internal GitHubAppTokenOverride? GitHubAppTokenOverride { get; set; }

        /// <summary>
        /// Gets or sets the set of NuGet package ID prefixes (the <c>*</c> wildcard is allowed) produced by the product
        /// itself and by the whole closure of its dependencies. When set, a build step that deletes these packages from
        /// the NuGet cache is inserted in front of all other build steps. This prevents stale packages from a previous
        /// build from leaking into this build.
        /// </summary>
        /// <summary>
        /// Gets or sets a value indicating whether this configuration starts containers without running in one,
        /// which is what a Docker test configuration does. Such a configuration has no
        /// <see cref="EngineeringPrepareImageBuildStep"/>, so it would otherwise miss the cleanup step that
        /// every containerised configuration gets.
        /// </summary>
        public bool StartsContainers { get; set; }

        /// <summary>
        /// Gets or sets the path, relative to the repository root, of the generated <c>CleanUpBuildAgent.ps1</c>,
        /// which deletes the stale packages before the build and removes what the build leaves on the agent
        /// afterwards. <c>null</c> emits neither step.
        /// </summary>
        public string? CleanUpBuildAgentScriptPath { get; set; }

        public TeamCityBuildConfiguration(
            string objectName,
            string name,
            string defaultBranch,
            string vcsId,
            BuildAgentRequirements? buildAgentRequirements = null )
        {
            this.ObjectName = objectName;
            this.Name = name;
            this.DefaultBranch = defaultBranch;
            this.VcsId = vcsId;
            this.BuildAgentRequirements = buildAgentRequirements;
        }

        public void GenerateTeamcityCode( TextWriter writer )
        {
            writer.WriteLine(
                $$"""
                  object {{this.ObjectName}} : BuildType({

                      name = "{{this.Name}}"

                  """ );

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
                var artifactRules = this.ArtifactRules.Replace( "\\n", "\n", StringComparison.Ordinal );

                if ( this.AdditionalArtifactRules != null )
                {
                    writer.WriteLine(
                        $"    artifactRules = \"\"\"{artifactRules}\n{string.Join( "\n", this.AdditionalArtifactRules.OrderBy( x => x, StringComparer.InvariantCulture ) )}\"\"\"" );
                }
                else
                {
                    writer.WriteLine( $"    artifactRules = \"\"\"{artifactRules}\"\"\"" );
                }

                writer.WriteLine();
            }

            // Add required build steps.
            var allBuildSteps = new List<BuildStep>();

            for ( var index = 0; index < this.BuildSteps!.Length; index++ )
            {
                var step = this.BuildSteps![index];

                AddBuildStep( step );

                void AddBuildStep( BuildStep newStep )
                {
                    newStep.InsertPrerequisites( allBuildSteps, AddBuildStep );
                    allBuildSteps.Add( newStep );
                }
            }

            // Insert, in front of all other build steps, a step that deletes from the NuGet cache every package the
            // build can reach, so that it cannot restore a stale copy of one instead of the artifacts it depends on.
            // Composite builds have no build steps, so they are skipped.
            //
            // Both this step and the one at the end name the generated script rather than carrying a command. The
            // script is where the logic is reviewable, testable and the same for a caller that is a container; a
            // settings file holding it inline was one very long line per build configuration, and the defect it was
            // fixed for -- a removal whose failure was silently absorbed -- is exactly what such a line hides.
            if ( this.CleanUpBuildAgentScriptPath != null && allBuildSteps.Count > 0 )
            {
                allBuildSteps.Insert(
                    0,
                    new PowerShellScriptBuildStep(
                        "CleanNuGetCache",
                        "Clean NuGet cache of produced and dependency packages",
                        this.CleanUpBuildAgentScriptPath,
                        "",
                        null ) );
            }

            // If any step uses Docker, add a cleanup step that always runs: it removes the containers this
            // build started, and then gives the agent a chance to undo what those containers did to the
            // working directory. What it does and why is in the script it calls.
            //
            // It belongs here rather than in DockerBuild.ps1. A build step runs after checkout, so anything
            // placed at the start of a build is already too late for the build that fails: the damage has to
            // be undone at the end of the build that caused it, which is what ExecutionMode.Always gives.
            if ( this.CleanUpBuildAgentScriptPath != null
                 && (this.StartsContainers || allBuildSteps.OfType<EngineeringPrepareImageBuildStep>().Any()) )
            {
                allBuildSteps.Add(
                    new PowerShellScriptBuildStep(
                        "DockerCleanup",
                        "Clean up the build agent",
                        this.CleanUpBuildAgentScriptPath,

                        // The label is what scopes the removal to the containers of this build. Removing every
                        // container of the agent would take out a build running beside this one.
                        "-After -BuildLabel %system.teamcity.buildType.id%_%build.number%",
                        null )
                    {
                        ExecutionMode = BuildStepExecutionMode.Always
                    } );
            }

            var buildParameters = new List<BuildConfigurationParameter>();

            buildParameters.AddRange( allBuildSteps.SelectMany( s => s.BuildConfigurationParameters ) );

            if ( this.Parameters != null )
            {
                buildParameters.AddRange( this.Parameters );
            }

            if ( buildParameters.Count > 0 )
            {
                writer.WriteLine(
                    $$"""
                          params {
                      {{string.Join( Environment.NewLine, buildParameters.Select( p => p.GenerateTeamCityCode() ) )}}
                          }

                      """ );
            }

            writer.WriteLine( "    vcs {" );

            if ( this.IsDefaultVcsRootUsed )
            {
                // We set the VCS root explicitly for consolidated as well builds to enable the DefaultBranch paramater.
                writer.WriteLine( $"""        root(AbsoluteId("{this.VcsId}"))""" );

                if ( allBuildSteps.Count == 0 )
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
                    writer.WriteLine(
                        $""""
                                 root(AbsoluteId("{sourceDependency.VcsId}"),
                                   """{sourceDependency.CheckoutRules}""")
                         """" );
                }
            }

            writer.WriteLine( "     checkoutMode = CheckoutMode.ON_AGENT" );
            writer.WriteLine( @"    }" );

            // Build steps.
            if ( allBuildSteps.Count > 0 )
            {
                if ( this.IsComposite )
                {
                    throw new InvalidOperationException( "Composite build cannot have build steps. Check if the build agent type is set." );
                }

                writer.WriteLine(
                    $$"""

                          steps {
                      """ );

                foreach ( var buildStep in allBuildSteps )
                {
                    writer.WriteLine( buildStep.GenerateTeamCityCode() );
                }

                writer.WriteLine( @"    }" );
            }

            if ( this.TimeoutInMinutes != null )
            {
                writer.WriteLine();
                writer.WriteLine( "    failureConditions {" );
                writer.WriteLine( $"        executionTimeoutMin = {this.TimeoutInMinutes.Value}" );
                writer.WriteLine( "    }" );
            }

            if ( !this.IsComposite && this.BuildAgentRequirements != null )
            {
                writer.WriteLine();
                writer.WriteLine( "    requirements {" );

                foreach ( var requirement in this.BuildAgentRequirements.Items )
                {
                    var comparison = requirement.ComparisonType switch
                    {
                        RequirementComparisonType.Equals => "equals",
                        RequirementComparisonType.Matches => "matches",
                        RequirementComparisonType.MoreThan => "moreThan",
                        RequirementComparisonType.DoesNotContain => "doesNotContain",
                        _ => "equals"
                    };

                    writer.WriteLine( $"        {comparison}(\"{requirement.Name}\", \"{requirement.Value}\")" );
                }

                writer.WriteLine( "    }" );
            }

            var requiresSwabra = allBuildSteps.Count > 0;
            var requiresSshAgent = this.IsSshAgentRequired;

            var requiresAnyFeatures =
                requiresSwabra || requiresSshAgent || this.RequiresCommitStatusPublisher || !this.GitHubAppBuildScopedTokens.IsEmpty;

            // Features.
            if ( requiresAnyFeatures )
            {
                writer.WriteLine(
                    $$"""

                          features {
                      """ );

                if ( requiresSwabra )
                {
                    writer.WriteLine(
                        $$"""
                                  swabra {
                                      filesCleanup = Swabra.FilesCleanup.BEFORE_BUILD
                                      lockingProcesses = Swabra.LockingProcessPolicy.KILL
                                      verbose = true
                                  }
                          """ );
                }

                foreach ( var buildScopedToken in this.GitHubAppBuildScopedTokens )
                {
                    // Issue a GitHub App installation token for the duration of the build. It is the only credential
                    // that GitHub accepts for an app, and it is what the features and the build steps below read.
                    // targetRepositories takes a newline-separated list, and there is no token standing for all
                    // repositories, so the ones the build reaches are enumerated.
                    var targetRepositories = string.Join( "\\n", buildScopedToken.TargetRepositories );

                    writer.WriteLine(
                        $$"""
                                  gitHubAppBuildScopedToken {
                                      parameterName = "{{buildScopedToken.ParameterName}}"
                                      connectionId = "{{buildScopedToken.ConnectionId}}"
                                      targetRepositories = "{{targetRepositories}}"
                                  }
                          """ );
                }

                if ( this.RequiresCommitStatusPublisher )
                {
                    // Report status to GitHub. The publisher reuses the credentials of the VCS root, which authenticates
                    // through the GitHub App connection, so no token has to be passed here.
                    writer.WriteLine(
                        $$"""
                                  commitStatusPublisher {
                                      vcsRootExtId = "{{this.VcsId}}"
                                      publisher = github {
                                          githubUrl = "https://api.github.com"
                                          authType = vcsRoot()
                                      }
                                  }
                          """ );

                    // Integrate with PRs. Like the commit status publisher, this reuses the credentials of the VCS root,
                    // which authenticates through the GitHub App connection.
                    writer.WriteLine(
                        $$"""
                                  pullRequests {
                                      vcsRootExtId = "{{this.VcsId}}"
                                      provider = github {
                                          authType = vcsRoot()
                                          filterTargetBranch = "+:refs/heads/{{this.DefaultBranch}}"
                                          filterAuthorRole = PullRequests.GitHubRoleFilter.EVERYBODY
                                      }
                                  }
                          """ );
                }

                if ( requiresSshAgent )
                {
                    // By convention, the SSH key name defaults to PostSharp.Engineering (used by all repositories that
                    // connect to Git over SSH). Deployment configurations can load a different uploaded key.
                    var sshAgentKeyName = this.SshAgentKeyName ?? "PostSharp.Engineering";

                    writer.WriteLine(
                        $$"""
                                  sshAgent {
                                      teamcitySshKey = "{{sshAgentKeyName}}"
                                  }
                          """ );
                }

                writer.WriteLine( $@"    }}" );
            }

            // Triggers.
            if ( this.BuildTriggers is { Length: > 0 } )
            {
                writer.WriteLine(
                    """

                        triggers {
                    """ );

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
                    $$"""

                          dependencies {
                      """ );

                foreach ( var dependency in this.SnapshotDependencies! )
                {
                    var objectName = dependency.IsAbsoluteId ? $"""AbsoluteId("{dependency.ObjectId}")""" : dependency.ObjectId;

                    var failureAction = dependency.FailureAction switch
                    {
                        FailureAction.FailToStart => "FAIL_TO_START",
                        FailureAction.AddProblem => "ADD_PROBLEM",
                        FailureAction.Ignore => "IGNORE",
                        FailureAction.Cancel => "CANCEL",
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    // ReuseBuilds.Any: no snapshot dependency, artifacts use lastSuccessful()
                    // ReuseBuilds.Successful: snapshot with synchronizeRevisions = false
                    // Default: normal snapshot dependency
                    if ( dependency.ReuseBuilds != ReuseBuilds.LastSuccessful )
                    {
                        writer.WriteLine(
                            $$"""
                                      snapshot({{objectName}}) {
                                               onDependencyFailure = FailureAction.{{failureAction}}
                                      }
                              """ );
                    }

                    if ( dependency.ArtifactRules != null )
                    {
                        var buildRule = dependency.ReuseBuilds == ReuseBuilds.LastSuccessful
                            ? dependency.Branch != null
                                ? $"\n                              buildRule = lastSuccessful(branch = \"{dependency.Branch}\")"
                                : "\n                              buildRule = lastSuccessful()"
                            : "";

                        writer.WriteLine(
                            $$"""

                                      artifacts({{objectName}}) { {{buildRule}}
                                          cleanDestination = {{(dependency.CleanDestination ? "true" : "false")}}
                                          artifactRules = "{{dependency.ArtifactRules}}"
                                      }
                              """ );
                    }
                }

                writer.WriteLine( $@"     }}" );
            }

            writer.WriteLine(
                $$"""

                  })
                  """ );
        }

    }
}