// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Triggers;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Files;

internal static class TeamCitySettingsFile
{
    internal static bool TryWrite( BuildContext context, CommonCommandSettings settings )
    {
        var product = context.Product;
        context.Console.WriteHeading( "Generating build integration scripts" );

        var configurations = new[] { BuildConfiguration.Debug, BuildConfiguration.Release, BuildConfiguration.Public };
        var teamCityBuildConfigurations = new List<TeamCityBuildConfiguration>();
        var isRepoRemoteSsh = product.DependencyDefinition.VcsRepository.IsSshAgentRequired;
        var defaultBranch = product.DependencyDefinition.Branch;
        var deploymentBranch = product.DependencyDefinition.PublishingBranch;
        var defaultBranchParameter = product.DependencyDefinition.VcsRepository.DefaultBranchParameter;
        var vcsRootId = TeamCityHelper.GetVcsRootId( product.DependencyDefinition );

        foreach ( var configuration in configurations )
        {
            var configurationInfo = product.Configurations[configuration];

            if ( !configurationInfo.ExportsToTeamCityBuild )
            {
                continue;
            }

            // Set artifact rules.
            var publicArtifactsDirectory =
                product.PublicArtifactsDirectory.Replace( "\\", "/", StringComparison.Ordinal );

            var privateArtifactsDirectory =
                product.GetPrivateArtifactsRelativeDirectory( configuration ).Replace( "\\", "/", StringComparison.Ordinal );

            var testResultsDirectory =
                product.TestResultsDirectory.Replace( "\\", "/", StringComparison.Ordinal );

            var logsDirectory = product.LogsDirectory.Replace( "\\", "/", StringComparison.Ordinal );
            var dumpsDirectory = product.DumpDirectory.Replace( "\\", "/", StringComparison.Ordinal );

            var deployedArtifactRules = $"+:{publicArtifactsDirectory}/**/*=>{publicArtifactsDirectory}";
            deployedArtifactRules += $@"\n+:{privateArtifactsDirectory}/**/*=>{privateArtifactsDirectory}";

            var publishedArtifactRules = deployedArtifactRules;
            publishedArtifactRules += $@"\n+:{testResultsDirectory}/**/*=>{testResultsDirectory}";
            publishedArtifactRules += $@"\n+:{logsDirectory}/**/*=>logs";
            publishedArtifactRules += $@"\n+:{dumpsDirectory}/**/*=>dumps";

            var additionalArtifactRules = product.DefaultArtifactRules;

            if ( configurationInfo.AdditionalArtifactRules != null )
            {
                additionalArtifactRules = product.DefaultArtifactRules.AddRange( configurationInfo.AdditionalArtifactRules );
            }

            if ( !DependenciesConfigurationFile.TryLoad( context, settings, configuration, out var dependenciesOverrideFile ) )
            {
                return false;
            }

            if ( !dependenciesOverrideFile.Fetch( context ) )
            {
                return false;
            }

            var dependencies =
                dependenciesOverrideFile.Dependencies.Select( x => (Name: x.Key,
                                                                    Definition: product.ProductFamily.GetDependencyDefinition( x.Key ),
                                                                    Source: x.Value) )
                    .Where( d => d.Definition.GenerateSnapshotDependency )
                    .Select( x => (x.Name, x.Definition, Configuration: VersionFileHelper.GetDependencyConfiguration( x.Definition, x.Source )) )
                    .ToList();

            var snapshotDependencies = dependencies
                .Select( d => new TeamCitySnapshotDependency(
                             d.Definition.CiConfiguration.BuildTypes[d.Configuration],
                             true,
                             $"+:{d.Definition.GetPrivateArtifactsDirectory( d.Configuration ).Replace( Path.DirectorySeparatorChar, '/' )}/**/*=>dependencies/{d.Name}" ) )
                .ToList();

            var sourceSnapshotDependencies = product.SourceDependencies.Where( d => d.GenerateSnapshotDependency )
                .Select( d => new TeamCitySnapshotDependency( d.CiConfiguration.BuildTypes[configuration], true ) );

            var buildDependencies = snapshotDependencies.Concat( sourceSnapshotDependencies ).OrderBy( d => d.ObjectId ).ToArray();

            var sourceDependencies = product.SourceDependencies.Select( d => new TeamCitySourceDependency(
                                                                            d.CiConfiguration.ProjectId.ToString(),
                                                                            true,
                                                                            $"+:. => {product.SourceDependenciesDirectory}/{d.Name}" ) )
                .ToArray();

            var teamCityBuildSteps = new List<TeamCityBuildStep>();

            if ( !product.UseDocker )
            {
                teamCityBuildSteps.Add( new TeamCityEngineeringCommandBuildStep( "PreKill", "Kill background processes before cleanup", "tools kill" ) );
            }

            var requiresUpstreamCheck =

                // The check is required.
                configurationInfo.RequiresUpstreamCheck

                // There is upstream product to check.
                && product.ProductFamily.UpstreamProductFamily != null

                // For products with the release branch, the check is done as part of the deployment preparation step.
                && product.DependencyDefinition.ReleaseBranch == null;

            if ( requiresUpstreamCheck )
            {
                teamCityBuildSteps.Add(
                    new TeamCityEngineeringCommandBuildStep(
                        "UpstreamCheck",
                        "Check pending upstream changes",
                        "tools git check-upstream",
                        areCustomArgumentsAllowed: true,
                        dockerSpec: product.DockerSpec ) );
            }

            teamCityBuildSteps.Add( new TeamCityEngineeringBuildBuildStep( configuration, true, product.DockerSpec, context.BuildTimeout ) );

            if ( !product.UseDocker )
            {
                teamCityBuildSteps.Add( new TeamCityEngineeringCommandBuildStep( "PostKill", "Kill background processes before next build", "tools kill" ) );
            }

            // The default branch for the public build cannot be set to the release branch,
            // because the schedulled build would not trigger the build on the develop branch
            // where the develop branch name differs.
            // Only the consolidated public build has the release branch as the default branch
            // and it expects that the release branch name is the same for each project.
            // If it happens that it's not, the build of the develop branch would be triggered
            // during the consolidated public build on such project, but the correct
            // one would be triggered during deployment.
            var teamCityBuildConfiguration = new TeamCityBuildConfiguration(
                $"{configuration}Build",
                configurationInfo.TeamCityBuildName ?? $"Build [{configuration}]",
                defaultBranch,
                defaultBranchParameter,
                vcsRootId,
                product.ResolvedBuildAgentRequirements )
            {
                BuildSteps = teamCityBuildSteps.ToArray(),
                ArtifactRules = publishedArtifactRules,
                AdditionalArtifactRules = additionalArtifactRules.ToArray(),
                BuildTriggers = configurationInfo.BuildTriggers,
                SnapshotDependencies = buildDependencies,
                SourceDependencies = sourceDependencies,
                IsSshAgentRequired = requiresUpstreamCheck && isRepoRemoteSsh
            };

            teamCityBuildConfigurations.Add( teamCityBuildConfiguration );

            TeamCityBuildConfiguration? teamCityDeploymentConfiguration = null;

            // Create a TeamCity configuration for Deploy.
            if ( configurationInfo.PrivatePublishers != null || configurationInfo.PublicPublishers != null )
            {
                TeamCityBuildStep CreatePublishBuildStep( bool isStandalone = false )
                    => new TeamCityEngineeringCommandBuildStep(
                        "Publish",
                        "Publish",
                        "publish",
                        $"--configuration {configuration}{(isStandalone ? " --standalone" : "")}",
                        true,
                        product.DockerSpec,
                        configurationInfo.DeploymentTimeout ?? product.DeploymentTimeout );

                if ( configurationInfo.ExportsToTeamCityDeploy )
                {
                    teamCityDeploymentConfiguration = new TeamCityBuildConfiguration(
                        $"{configuration}Deployment",
                        configurationInfo.TeamCityDeploymentName ?? $"Deploy [{configuration}]",
                        deploymentBranch,
                        defaultBranchParameter,
                        vcsRootId,
                        product.ResolvedBuildAgentRequirements )
                    {
                        BuildSteps = [CreatePublishBuildStep()],
                        IsDeployment = true,
                        SnapshotDependencies = buildDependencies.Where( d => d.ArtifactRules != null )
                            .Concat( [new TeamCitySnapshotDependency( teamCityBuildConfiguration.ObjectName, false, deployedArtifactRules )] )
                            .Concat(
                                product.ParametrizedDependencies.Select( d => d.Definition )
                                    .Union( product.SourceDependencies )
                                    .Where( d => d is { GenerateSnapshotDependency: true, CiConfiguration.DeploymentBuildType: not null } )
                                    .Select( d => new TeamCitySnapshotDependency( d.CiConfiguration.DeploymentBuildType!, true ) ) )
                            .OrderBy( d => d.ObjectId )
                            .ToArray(),
                        IsSshAgentRequired = isRepoRemoteSsh
                    };

                    teamCityBuildConfigurations.Add( teamCityDeploymentConfiguration );
                }

                if ( configurationInfo.ExportsToTeamCityDeployWithoutDependencies )
                {
                    // The standalone deployment doesn't expect pre-publishing and post-publishing step to be triggered,
                    // so it's done from the develop branch.
                    teamCityDeploymentConfiguration = new TeamCityBuildConfiguration(
                        objectName: $"{configuration}DeploymentNoDependency",
                        name: "Standalone " + (configurationInfo.TeamCityDeploymentName ?? $"Deploy [{configuration}]"),
                        defaultBranch,
                        defaultBranchParameter,
                        vcsRootId,
                        buildAgentRequirements: product.ResolvedBuildAgentRequirements )
                    {
                        BuildSteps = [CreatePublishBuildStep( true )],
                        IsDeployment = true,
                        SnapshotDependencies = buildDependencies.Where( d => d.ArtifactRules != null )
                            .Concat( [new TeamCitySnapshotDependency( teamCityBuildConfiguration.ObjectName, false, deployedArtifactRules )] )
                            .OrderBy( d => d.ObjectId )
                            .ToArray(),
                        IsSshAgentRequired = isRepoRemoteSsh
                    };

                    teamCityBuildConfigurations.Add( teamCityDeploymentConfiguration );
                }
            }

            // Create a TeamCity configuration for Swap.
            if ( configurationInfo is { Swappers: { }, SwapAfterPublishing: false } )
            {
                var swapDependencies = new List<TeamCitySnapshotDependency>();

                if ( teamCityDeploymentConfiguration != null )
                {
                    swapDependencies.Add( new TeamCitySnapshotDependency( teamCityDeploymentConfiguration.ObjectName, false ) );
                    swapDependencies.Add( new TeamCitySnapshotDependency( teamCityBuildConfiguration.ObjectName, false ) );
                }

                teamCityBuildConfigurations.Add(
                    new TeamCityBuildConfiguration(
                        objectName: $"{configuration}Swap",
                        name: configurationInfo.TeamCitySwapName ?? $"Swap [{configuration}]",
                        deploymentBranch,
                        defaultBranchParameter,
                        vcsRootId,
                        buildAgentRequirements: product.ResolvedBuildAgentRequirements )
                    {
                        BuildSteps =
                        [
                            new TeamCityEngineeringCommandBuildStep(
                                "Swap",
                                "Swap",
                                "swap",
                                $"--configuration {configuration}",
                                true,
                                product.DockerSpec,
                                configurationInfo.SwapTimeout ?? product.SwapTimeout )
                        ],
                        IsDeployment = true,
                        SnapshotDependencies = swapDependencies.OrderBy( d => d.ObjectId ).ToArray()
                    } );
            }
        }

        // Only versioned products that don't have consolidated version bump can be bumped individually.
        if ( !product.ProductFamily.HasConsolidatedBuild && product.DependencyDefinition.IsVersioned )
        {
            var dependencies = product.ParametrizedDependencies;

            if ( dependencies != null! )
            {
                teamCityBuildConfigurations.Add(
                    new TeamCityBuildConfiguration(
                        objectName: "VersionBump",
                        name: $"Version Bump",
                        defaultBranch,
                        defaultBranchParameter,
                        vcsRootId,
                        buildAgentRequirements: product.ResolvedBuildAgentRequirements )
                    {
                        BuildSteps =
                        [
                            new TeamCityEngineeringCommandBuildStep(
                                "Bump",
                                "Bump",
                                "bump",
                                areCustomArgumentsAllowed: true,
                                dockerSpec: product.DockerSpec,
                                timeout: product.VersionBumpTimeout )
                        ],
                        IsSshAgentRequired = isRepoRemoteSsh
                    } );
            }
        }

        // Create a TeamCity configuration for downstream merge.
        if ( product.ProductFamily.DownstreamProductFamily != null )
        {
            var snapshotDependencies = product.Configurations[BuildConfiguration.Debug].ExportsToTeamCityBuild
                ? new[] { new TeamCitySnapshotDependency( "DebugBuild", false ) }
                : null;

            teamCityBuildConfigurations.Add(
                new TeamCityBuildConfiguration(
                    "DownstreamMerge",
                    "Downstream Merge",
                    defaultBranch,
                    defaultBranchParameter,
                    vcsRootId,
                    product.ResolvedBuildAgentRequirements )
                {
                    BuildSteps =
                    [
                        new TeamCityEngineeringCommandBuildStep(
                            "DownstreamMerge",
                            "Merge downstream",
                            "tools git merge-downstream",
                            areCustomArgumentsAllowed: true,
                            dockerSpec: product.DockerSpec,
                            timeout: product.DownstreamMergeTimeout )
                    ],
                    SnapshotDependencies = snapshotDependencies,
                    BuildTriggers = [new SourceBuildTrigger()],
                    IsSshAgentRequired = isRepoRemoteSsh
                } );
        }

        // Add from extensions.
        foreach ( var extension in product.Extensions )
        {
            if ( !extension.AddTeamcityBuildConfiguration( context, teamCityBuildConfigurations ) )
            {
                return false;
            }
        }

        var teamCityProject = new TeamCityProject( teamCityBuildConfigurations.ToArray() );

        GeneratePom( context, product.DependencyDefinition.CiConfiguration.ProjectId.Id, product.DependencyDefinition.CiConfiguration.BaseUrl );
        GenerateTeamCityConfiguration( context, teamCityProject );

        return true;
    }

    private static void GeneratePom( BuildContext context, string projectObjectName, string tcUrl )
    {
        TextFileHelper.WriteIfDifferent(
            Path.Combine( context.RepoDirectory, ".teamcity", "pom.xml" ),
            @$"<?xml version=""1.0""?>
<project>
  <modelVersion>4.0.0</modelVersion>
  <name>{projectObjectName} Config DSL Script</name>
  <groupId>{projectObjectName}</groupId>
  <artifactId>{projectObjectName}_dsl</artifactId>
  <version>1.0-SNAPSHOT</version>

  <parent>
    <groupId>org.jetbrains.teamcity</groupId>
    <artifactId>configs-dsl-kotlin-parent</artifactId>
    <version>1.0-SNAPSHOT</version>
  </parent>

  <repositories>
    <repository>
      <id>jetbrains-all</id>
      <url>https://download.jetbrains.com/teamcity-repository</url>
      <snapshots>
        <enabled>true</enabled>
      </snapshots>
    </repository>
    <repository>
      <id>teamcity-server</id>
      <url>{tcUrl}/app/dsl-plugins-repository</url>
      <snapshots>
        <enabled>true</enabled>
      </snapshots>
    </repository>
  </repositories>

  <pluginRepositories>
    <pluginRepository>
      <id>JetBrains</id>
      <url>https://download.jetbrains.com/teamcity-repository</url>
    </pluginRepository>
  </pluginRepositories>

  <build>
    <sourceDirectory>${{basedir}}</sourceDirectory>
    <plugins>
      <plugin>
        <artifactId>kotlin-maven-plugin</artifactId>
        <groupId>org.jetbrains.kotlin</groupId>
        <version>${{kotlin.version}}</version>

        <configuration/>
        <executions>
          <execution>
            <id>compile</id>
            <phase>process-sources</phase>
            <goals>
              <goal>compile</goal>
            </goals>
          </execution>
          <execution>
            <id>test-compile</id>
            <phase>process-test-sources</phase>
            <goals>
              <goal>test-compile</goal>
            </goals>
          </execution>
        </executions>
      </plugin>
      <plugin>
        <groupId>org.jetbrains.teamcity</groupId>
        <artifactId>teamcity-configs-maven-plugin</artifactId>
        <version>${{teamcity.dsl.version}}</version>
        <configuration>
          <format>kotlin</format>
          <dstDir>target/generated-configs</dstDir>
        </configuration>
      </plugin>
    </plugins>
  </build>

  <dependencies>
    <dependency>
      <groupId>org.jetbrains.teamcity</groupId>
      <artifactId>configs-dsl-kotlin-latest</artifactId>
      <version>${{teamcity.dsl.version}}</version>
      <scope>compile</scope>
    </dependency>
    <dependency>
      <groupId>org.jetbrains.teamcity</groupId>
      <artifactId>configs-dsl-kotlin-plugins-latest</artifactId>
      <version>1.0-SNAPSHOT</version>
      <type>pom</type>
      <scope>compile</scope>
    </dependency>
    <dependency>
      <groupId>org.jetbrains.kotlin</groupId>
      <artifactId>kotlin-stdlib-jdk8</artifactId>
      <version>${{kotlin.version}}</version>
      <scope>compile</scope>
    </dependency>
    <dependency>
      <groupId>org.jetbrains.kotlin</groupId>
      <artifactId>kotlin-script-runtime</artifactId>
      <version>${{kotlin.version}}</version>
      <scope>compile</scope>
    </dependency>
  </dependencies>
</project>",
            context );
    }

    internal static void GenerateTeamCityConfiguration( BuildContext context, TeamCityProject project )
    {
        var content = new StringWriter();
        project.GenerateTeamcityCode( content );

        var filePath = Path.Combine( context.RepoDirectory, ".teamcity", "settings.kts" );

        TextFileHelper.WriteIfDifferent( filePath, content.ToString()!, context );
    }
}