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
        /// Gets or sets the NuGet package directories to delete before the build restores anything. See
        /// <see cref="NuGetCachePatterns"/>.
        /// </summary>
        public string[]? NuGetCachePackagePatterns { get; set; }

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
            if ( this.NuGetCachePackagePatterns is { Length: > 0 } && allBuildSteps.Count > 0 )
            {
                allBuildSteps.Insert(
                    0,
                    new PowerShellCommandBuildStep(
                        "CleanNuGetCache",
                        "Clean NuGet cache of produced and dependency packages",
                        GenerateNuGetCacheCleanupCommand( this.NuGetCachePackagePatterns ),
                        null ) );
            }

            // If any step uses Docker, add a cleanup step that always runs: it removes the containers this
            // build started, and then gives the agent a chance to undo what those containers did to the
            // working directory.
            //
            // The second part exists because a container runs as root while the agent does not, and the
            // repository is a bind mount, so whatever the build wrote into it is owned by root on the host.
            // The agent user cannot unlink those files, and a checkout directory belongs to a VCS root rather
            // than to one build configuration, so the next build OF ANY KIND on that agent fails at checkout
            // with "Error while applying patch" and thousands of "failed to remove ...: Permission denied".
            // Swabra detects them, logs "unable to delete" for each and continues, so nothing catches it
            // earlier.
            //
            // It belongs here rather than in DockerBuild.ps1. A build step runs after checkout, so anything
            // placed at the start of a build is already too late for the build that fails: the damage has to
            // be undone at the end of the build that caused it, which is what ExecutionMode.Always gives.
            // The containers are removed first, so nothing is still writing when the agent's command runs.
            //
            // What that command is, is the agent's business. BUILDAGENT_CLEANUP_SCRIPT names it -- typically
            // "sudo /opt/buildAgent/bin/chown-all.sh" on the Linux agents -- and unset means no command,
            // which is every Windows agent, where the question does not arise.
            if ( this.StartsContainers || allBuildSteps.OfType<EngineeringPrepareImageBuildStep>().Any() )
            {
                allBuildSteps.Add(
                    new PowerShellCommandBuildStep(
                        "DockerCleanup",
                        "Cleanup Docker containers",
                        "$label = \"%system.teamcity.buildType.id%_%build.number%\"; $ids = docker ps -a -q --filter \"label=postsharp.build=$label\"; if ($ids) { docker rm -f $ids 2>&1 | Out-Null }; if ($env:BUILDAGENT_CLEANUP_SCRIPT) { Write-Host \"Running the agent cleanup script: $($env:BUILDAGENT_CLEANUP_SCRIPT)\"; try { Invoke-Expression $env:BUILDAGENT_CLEANUP_SCRIPT; if ($LASTEXITCODE -ne 0) { Write-Host \"The agent cleanup script exited with code $LASTEXITCODE.\" } } catch { Write-Host \"The agent cleanup script failed: $_\" } }",
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

        /// <summary>
        /// Generates the PowerShell script of the step that deletes, from the NuGet global packages folder of the agent,
        /// every package directory matching one of the given <paramref name="packagePatterns"/>. The script honours the
        /// <c>NUGET_PACKAGES</c> environment variable and otherwise falls back to the default location in the user
        /// profile (<c>$HOME/.nuget/packages</c>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// A directory that survives fails the build. What this replaced passed <c>-ErrorAction SilentlyContinue</c> and
        /// reported how much it had removed while never reporting what it could not: on a Linux agent it found some
        /// thirty directories, removed none of them and exited 0, so every build there restored whatever an earlier
        /// build had left in the cache. Finding nothing to delete is still success -- an empty cache is the normal state
        /// of an agent that has just been cleaned.
        /// </para>
        /// <para>
        /// The one survivor that is not a defect is a directory another account owns. A container runs as root while the
        /// agent does not, and the cache is a bind mount, so what a container of an earlier build extracted cannot be
        /// unlinked here at all -- no error handling changes that. Those are named and left to the container that is
        /// about to restore them, which runs as root and does fail the build when it cannot remove them: see
        /// <c>New-NuGetCacheCleanupScript</c> in <c>DockerBuild.ps1</c>. Failing here instead would mean no build could
        /// ever run again on such an agent, since the cache is shared by every build configuration.
        /// </para>
        /// <para>
        /// Internal rather than private because the tests run the script this returns, in PowerShell, against a
        /// directory laid out like a NuGet cache. Asserting on its text would say nothing about the defect it replaced,
        /// whose text read correctly.
        /// </para>
        /// </remarks>
        internal static string GenerateNuGetCacheCleanupCommand( IEnumerable<string> packagePatterns )
        {
            // NuGetCachePatterns.GetPatterns has already lower-cased these and rejected anything that is not a package
            // identifier or the '*' wildcard, so quoting them is all that is left to do here.
            var patterns = string.Join( ", ", packagePatterns.Select( p => $"'{p}'" ) );

            // Line endings are normalized because the script is escaped into a Kotlin string literal, and because the
            // agent that runs it may be Unix.
            return
                $$"""
                  $patterns = @({{patterns}})

                  $nugetPackages = if ( $env:NUGET_PACKAGES ) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget' 'packages' }

                  if ( -not ( Test-Path -LiteralPath $nugetPackages ) )
                  {
                      Write-Host "NuGet packages folder not found: $nugetPackages"
                      exit 0
                  }

                  # The identifier of the account this step runs as, on Unix only, where it decides whether a directory
                  # that cannot be removed is a defect or a directory belonging to root that a container will remove. A
                  # Unix without `id` gets the Windows treatment, where every survivor is a defect.
                  $ownUserId = $null

                  if ( -not $IsWindows )
                  {
                      try { $ownUserId = [int](& id -u) } catch { $ownUserId = $null }
                  }

                  $removedDirectories = 0
                  $removedFiles = 0
                  $failures = @()
                  $ownedByAnotherAccount = @()

                  foreach ( $pattern in $patterns )
                  {
                      foreach ( $directory in @( Get-ChildItem -LiteralPath $nugetPackages -Directory -Filter $pattern -ErrorAction SilentlyContinue ) )
                      {
                          $path = $directory.FullName
                          $files = @( Get-ChildItem -LiteralPath $path -Recurse -File -ErrorAction SilentlyContinue ).Count
                          Write-Host "Removing NuGet cache directory: $path ($files file(s))"

                          $failure = $null

                          try
                          {
                              Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
                          }
                          catch
                          {
                              $failure = $_.Exception.Message
                          }

                          if ( -not ( Test-Path -LiteralPath $path ) )
                          {
                              $removedDirectories++
                              $removedFiles += $files
                              continue
                          }

                          if ( -not $failure )
                          {
                              $failure = 'the directory is still present after the removal'
                          }

                          # Everything still there is tested, not the directory itself: NuGet creates the directory of a
                          # package the first time anything restores it, so the agent may well own that while the version
                          # directory a container extracted underneath it belongs to root.
                          $ownedElsewhere = @()

                          if ( $null -ne $ownUserId )
                          {
                              $entries = @( Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue ) +
                                  @( Get-ChildItem -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue )

                              $ownedElsewhere = @( $entries | Where-Object { $_.UnixStat.UserId -ne $ownUserId } )
                          }

                          if ( $ownedElsewhere.Count -gt 0 )
                          {
                              $ownedByAnotherAccount += $path
                          }
                          else
                          {
                              $failures += "${path}: $failure"
                          }
                      }
                  }

                  Write-Host "Removed $removedDirectories package directory(ies) and $removedFiles file(s) from the NuGet cache."

                  if ( $ownedByAnotherAccount.Count -gt 0 )
                  {
                      Write-Host "$($ownedByAnotherAccount.Count) directory(ies) belong to another account, having been extracted by a container of an earlier build. The container that restores them next deletes them, and fails the build if it cannot:"
                      $ownedByAnotherAccount | ForEach-Object { Write-Host "  $_" }
                  }

                  if ( $failures.Count -gt 0 )
                  {
                      Write-Host "The NuGet cache could not be cleaned, so this build would restore stale packages:" -ForegroundColor Red
                      $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
                      exit 1
                  }
                  """.ReplaceLineEndings( "\n" );
        }
    }
}