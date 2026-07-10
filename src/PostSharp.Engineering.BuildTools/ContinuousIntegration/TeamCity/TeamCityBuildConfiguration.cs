// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Arguments;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Triggers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity
{
    internal class TeamCityBuildConfiguration
    {
        public string ObjectName { get; }

        public string Name { get; }

        public string DefaultBranch { get; }

        public string VcsId { get; }

        public BuildAgentRequirements? BuildAgentRequirements { get; }

        public BuildStep[]? BuildSteps { get; init; }

        public bool IsDeployment { get; init; }

        public bool IsComposite => this.BuildAgentRequirements == null;

        public bool IsSshAgentRequired { get; init; }

        public string? ArtifactRules { get; init; }

        public string[]? AdditionalArtifactRules { get; init; }

        public IBuildTrigger[]? BuildTriggers { get; init; }

        public TeamCitySnapshotDependency[]? SnapshotDependencies { get; init; }

        public TeamCitySourceDependency[]? SourceDependencies { get; init; }

        public bool IsDefaultVcsRootUsed { get; init; } = true;

        public BuildConfigurationParameter[]? Parameters { get; init; }

        public bool RequiresCommitStatusPublisher { get; init; }

        /// <summary>
        /// Gets or sets the settings of the build feature that issues a GitHub App installation token for the duration
        /// of the build and exposes it as the <c>GITHUB_TOKEN</c> environment variable. <c>null</c> when the repository
        /// is not hosted on GitHub, or when its product family has no GitHub App connection.
        /// </summary>
        public GitHubAppBuildScopedTokenSettings? GitHubAppBuildScopedToken { get; set; }

        /// <summary>
        /// Gets or sets the set of NuGet package ID prefixes (the <c>*</c> wildcard is allowed) produced by the product
        /// itself and by the whole closure of its dependencies. When set, a build step that deletes these packages from
        /// the NuGet cache is inserted in front of all other build steps. This prevents stale packages from a previous
        /// build from leaking into this build.
        /// </summary>
        public string[]? NuGetCachePackagePrefixes { get; set; }

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

            // Insert, in front of all other build steps, a step that deletes from the NuGet cache all packages produced
            // by the product itself and by the whole closure of its dependencies. Composite builds have no build steps,
            // so they are skipped.
            if ( this.NuGetCachePackagePrefixes is { Length: > 0 } && allBuildSteps.Count > 0 )
            {
                allBuildSteps.Insert(
                    0,
                    new PowerShellCommandBuildStep(
                        "CleanNuGetCache",
                        "Clean NuGet cache of produced and dependency packages",
                        GenerateNuGetCacheCleanupCommand( this.NuGetCachePackagePrefixes ),
                        null ) );
            }

            // If any step uses Docker, add a cleanup step that always runs to remove orphaned containers.
            if ( allBuildSteps.OfType<EngineeringPrepareImageBuildStep>().Any() )
            {
                allBuildSteps.Add(
                    new PowerShellCommandBuildStep(
                        "DockerCleanup",
                        "Cleanup Docker containers",
                        "$label = \"%system.teamcity.buildType.id%_%build.number%\"; $ids = docker ps -a -q --filter \"label=postsharp.build=$label\"; if ($ids) { docker rm -f $ids 2>&1 | Out-Null }",
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
                    var objectName = sourceDependency.IsAbsoluteId ? $"""AbsoluteId("{sourceDependency.VcsId}")""" : sourceDependency.VcsId;

                    writer.WriteLine(
                        $""""
                                 root({objectName},
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
                        _ => "equals"
                    };

                    writer.WriteLine( $"        {comparison}(\"{requirement.Name}\", \"{requirement.Value}\")" );
                }

                writer.WriteLine( "    }" );
            }

            var requiresSwabra = allBuildSteps.Count > 0;
            var requiresSshAgent = this.IsSshAgentRequired;

            var requiresAnyFeatures =
                requiresSwabra || requiresSshAgent || this.RequiresCommitStatusPublisher || this.GitHubAppBuildScopedToken != null;

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

                if ( this.GitHubAppBuildScopedToken != null )
                {
                    // Issue a GitHub App installation token for the duration of the build. It is the only credential
                    // that GitHub accepts for an app, and it is what the features and the build steps below read.
                    writer.WriteLine(
                        $$"""
                                  gitHubAppBuildScopedToken {
                                      parameterName = "env.{{EnvironmentVariableNames.GitHubToken}}"
                                      connectionId = "{{this.GitHubAppBuildScopedToken.ConnectionId}}"
                                      targetRepositories = "{{this.GitHubAppBuildScopedToken.TargetRepository}}"
                                  }
                          """ );
                }

                if ( this.RequiresCommitStatusPublisher )
                {
                    // Report status to GitHub.
                    writer.WriteLine(
                        $$"""
                              commitStatusPublisher {
                                  vcsRootExtId = "{{this.VcsId}}"
                                  publisher = github {
                                      githubUrl = "https://api.github.com"
                                      authType = personalToken {
                                          token = "%env.{{EnvironmentVariableNames.GitHubToken}}%"
                                      }
                                  }
                              }
                          """ );

                    // Integrate with PRs.
                    writer.WriteLine(
                        $$"""
                          pullRequests {
                                 vcsRootExtId = "{{this.VcsId}}"
                                  provider = github {
                                      authType = token {
                                          token = "%env.{{EnvironmentVariableNames.GitHubToken}}%"
                                      }
                                     filterTargetBranch = "+:refs/heads/{{this.DefaultBranch}}"
                                     filterAuthorRole = PullRequests.GitHubRoleFilter.EVERYBODY
                                 }
                             }


                          """ );
                }

                if ( requiresSshAgent )
                {
                    writer.WriteLine(
                        $$"""
                                  sshAgent {
                                      // By convention, the SSH key name is always PostSharp.Engineering for all repositories using SSH to connect.
                                      teamcitySshKey = "PostSharp.Engineering"
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
                                          cleanDestination = true
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

        /// <summary>
        /// Generates a single-line PowerShell command that deletes, from the NuGet global packages folder, all package
        /// directories whose name matches one of the given <paramref name="packagePrefixes"/>. The command honors the
        /// <c>NUGET_PACKAGES</c> environment variable and otherwise falls back to the default location in the user profile
        /// (<c>$HOME/.nuget/packages</c>). It logs each removed directory with its file count and prints a summary of how
        /// many directories and files were deleted.
        /// </summary>
        private static string GenerateNuGetCacheCleanupCommand( IEnumerable<string> packagePrefixes )
        {
            // NuGet stores packages in lower-case directories, so the match patterns are lower-cased here. Single quotes
            // are doubled to remain valid inside a PowerShell single-quoted string literal.
            var patterns = string.Join(
                ", ",
                packagePrefixes.Select( p => "'" + p.Replace( "'", "''", StringComparison.Ordinal ).ToLowerInvariant() + "'" ) );

            return
                "$nugetPackages = if ( $env:NUGET_PACKAGES ) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget' 'packages' }; "
                + "$removedDirs = 0; $removedFiles = 0; "
                + "if ( Test-Path -LiteralPath $nugetPackages ) { "
                + "foreach ( $pattern in @(" + patterns + ") ) { "
                + "Get-ChildItem -LiteralPath $nugetPackages -Directory -Filter $pattern -ErrorAction SilentlyContinue | "
                + "ForEach-Object { "
                + "$files = @( Get-ChildItem -LiteralPath $_.FullName -Recurse -File -ErrorAction SilentlyContinue ).Count; "
                + "Write-Host \"Removing NuGet cache directory: $($_.FullName) ($files file(s))\"; "
                + "Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue; "
                + "if ( -not ( Test-Path -LiteralPath $_.FullName ) ) { $removedDirs++; $removedFiles += $files } } "
                + "} Write-Host \"Removed $removedDirs package directory(ies) and $removedFiles file(s) from the NuGet cache.\"; "
                + "} else { Write-Host \"NuGet packages folder not found: $nugetPackages\" }";
        }
    }
}