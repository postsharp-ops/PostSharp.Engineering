// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Publishing;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;

internal static class TeamCitySettingsFile
{
    internal static bool TryWrite( BuildContext context )
    {
        var product = context.Product;
        context.Console.WriteHeading( "Generating build integration scripts" );

        var configurations = new[] { BuildConfiguration.Debug, BuildConfiguration.Release, BuildConfiguration.Public };
        var teamCityBuildConfigurations = new List<TeamCityBuildConfiguration>();
        var teamCityBuildBuildConfigurations = new Dictionary<BuildConfiguration, TeamCityBuildConfiguration>();

        // Create product-level properties once
        var productProperties = new ProductProperties( product );

        foreach ( var configuration in configurations )
        {
            var configurationInfo = product.Configurations[configuration];

            if ( !configurationInfo.ExportsToTeamCityBuild )
            {
                continue;
            }

            var additionalArtifactRules = product.DefaultArtifactRules;

            if ( configurationInfo.AdditionalArtifactRules != null )
            {
                additionalArtifactRules = product.DefaultArtifactRules.AddRange( configurationInfo.AdditionalArtifactRules );
            }

            // Create configuration-specific properties
            var configurationProperties = new ConfigurationProperties( product, configuration );

            // Set artifact rules using both ProductProperties and ConfigurationProperties.
            var deployedArtifactRules = $"+:{productProperties.PublicArtifactsDirectory}/**/*=>{productProperties.PublicArtifactsDirectory}";
            deployedArtifactRules += $@"\n+:{configurationProperties.PrivateArtifactsDirectory}/**/*=>{configurationProperties.PrivateArtifactsDirectory}";

            var publishedArtifactRules = deployedArtifactRules;
            publishedArtifactRules += $@"\n+:{productProperties.TestResultsDirectory}/**/*=>{productProperties.TestResultsDirectory}";
            publishedArtifactRules += $@"\n+:{productProperties.LogsDirectory}/**/*=>logs";
            publishedArtifactRules += $@"\n+:{productProperties.DumpsDirectory}/**/*=>dumps";

            var teamCityBuildConfiguration = CreateBuildConfiguration(
                context,
                productProperties,
                configurationProperties,
                publishedArtifactRules,
                additionalArtifactRules );

            teamCityBuildConfigurations.Add( teamCityBuildConfiguration );
            teamCityBuildBuildConfigurations.Add( configuration, teamCityBuildConfiguration );

            TeamCityBuildConfiguration? teamCityDeploymentConfiguration = null;

            // SSH publishers are inert markers: they are deployed by native TeamCity SSH runners in their own
            // configuration, not by the 'b publish' step. So they are handled separately from the other publishers.
            var allPublishers = ( configurationInfo.PublicPublishers ?? [] ).Concat( configurationInfo.PrivatePublishers ?? [] ).ToList();
            var sshPublishers = allPublishers.OfType<SshPublisher>().ToArray();
            var hasNonSshPublisher = allPublishers.Any( p => p is not SshPublisher );

            // Create a TeamCity configuration for the publisher-based Deploy (skipped when the only publishers are the
            // inert SSH markers, which get their own configuration below).
            if ( hasNonSshPublisher )
            {
                if ( configurationInfo.ExportsToTeamCityDeploy )
                {
                    teamCityDeploymentConfiguration = CreateDeployConfiguration(
                        productProperties,
                        configurationProperties,
                        teamCityBuildConfiguration,
                        deployedArtifactRules,
                        false );

                    teamCityBuildConfigurations.Add( teamCityDeploymentConfiguration );
                }

                if ( configurationInfo.ExportsToTeamCityDeployWithoutDependencies )
                {
                    teamCityDeploymentConfiguration = CreateDeployConfiguration(
                        productProperties,
                        configurationProperties,
                        teamCityBuildConfiguration,
                        deployedArtifactRules,
                        true );

                    teamCityBuildConfigurations.Add( teamCityDeploymentConfiguration );
                }
            }

            // Create a TeamCity configuration for SSH deployment, built from native SSH runners.
            if ( sshPublishers.Length > 0 )
            {
                var sshDeploymentConfiguration = CreateSshDeployConfiguration(
                    productProperties,
                    configurationProperties,
                    teamCityBuildConfiguration,
                    deployedArtifactRules,
                    sshPublishers );

                teamCityBuildConfigurations.Add( sshDeploymentConfiguration );
            }

            // Create a TeamCity configuration for Swap.
            if ( configurationInfo is { Swappers: { Length: > 0 }, SwapAfterPublishing: false } )
            {
                var swapConfiguration = CreateSwapConfiguration(
                    productProperties,
                    configurationProperties,
                    teamCityDeploymentConfiguration,
                    teamCityBuildConfiguration,
                    deployedArtifactRules );

                teamCityBuildConfigurations.Add( swapConfiguration );
            }
        }

        // Only versioned products that don't have consolidated version bump can be bumped individually.
        if ( !product.ProductFamily.HasConsolidatedProduct && product.DependencyDefinition.IsVersioned )
        {
            var dependencies = product.ParametrizedDependencies;

            if ( dependencies != null! )
            {
                var bumpConfiguration = CreateBumpConfiguration( productProperties );

                teamCityBuildConfigurations.Add( bumpConfiguration );
            }
        }

        // Create a TeamCity configuration for upstream merge.
        if ( product.ProductFamily.UpstreamProductFamily != null )
        {
            var upstreamMergeConfiguration = CreateUpstreamMergeConfiguration( productProperties );

            teamCityBuildConfigurations.Add( upstreamMergeConfiguration );
        }

        // Add product-defined.
        foreach ( var additional in product.AdditionalCiBuildConfigurations )
        {
            var configuration = additional.TeamCityBuildConfiguration( productProperties, teamCityBuildBuildConfigurations );
            teamCityBuildConfigurations.Add( configuration );
        }

        // Add from extensions.
        foreach ( var extension in product.Extensions )
        {
            if ( !extension.AddTeamcityBuildConfiguration( context, teamCityBuildConfigurations ) )
            {
                return false;
            }
        }

        // Insert, in front of every build configuration, a step that cleans the NuGet cache of all packages produced by
        // the current repo and by the whole closure of its dependencies, so stale packages cannot leak into the build.
        var nugetCachePackagePrefixes = GetNuGetCachePackagePrefixes( product );

        if ( nugetCachePackagePrefixes.Length > 0 )
        {
            foreach ( var teamCityBuildConfiguration in teamCityBuildConfigurations )
            {
                teamCityBuildConfiguration.NuGetCachePackagePrefixes = nugetCachePackagePrefixes;
            }
        }

        // A GitHub App has no long-lived credential, so every build configuration issues its own installation token.
        if ( product.DependencyDefinition.VcsRepository is GitHubRepository gitHubRepository
             && product.DependencyDefinition.EffectiveGitHubAppConnectionId is { } gitHubAppConnectionId )
        {
            var buildScopedToken = new GitHubAppBuildScopedTokenSettings( gitHubAppConnectionId, gitHubRepository.Name );

            foreach ( var teamCityBuildConfiguration in teamCityBuildConfigurations )
            {
                teamCityBuildConfiguration.GitHubAppBuildScopedToken = buildScopedToken;
            }
        }

        var teamCityProject = new TeamCityProject( teamCityBuildConfigurations.ToArray(), [] );

        GeneratePom( context, product.DependencyDefinition.CiConfiguration.ProjectId.Id, product.DependencyDefinition.CiConfiguration.BaseUrl );
        GenerateTeamCityConfiguration( context, teamCityProject );

        return true;
    }

    private static TeamCityBuildConfiguration CreateDeployConfiguration(
        ProductProperties productProperties,
        ConfigurationProperties configurationProperties,
        TeamCityBuildConfiguration teamCityBuildConfiguration,
        string deployedArtifactRules,
        bool isStandalone )
    {
        var product = productProperties.Product;

        BuildStep step =
            new EngineeringCommandBuildStep(
                "Publish",
                "Publish",
                "publish",
                $"--configuration {configurationProperties.Configuration}{(isStandalone ? " --standalone" : "")}",
                true,
                product.DockerSpec,
                configurationProperties.BuildConfigurationInfo.DeploymentTimeout ?? product.DeploymentTimeout );

        var snapshotDependencies = configurationProperties.SnapshotDependenciesForBuildConfiguration.Where( d => d.ArtifactRules != null )
            .Concat( [new TeamCitySnapshotDependency( teamCityBuildConfiguration.ObjectName, false, deployedArtifactRules )] );

        if ( !isStandalone )
        {
            // Aliased + LastSuccessful deps must be excluded: we don't snapshot-depend on them for the consumer's build,
            // and the same applies to deployment.
            var parametrizedDeploymentDependencies = product.ParametrizedDependencies
                .Where( d => d.ArtifactPickup == Dependencies.Model.DependencyArtifactPickup.Snapshot )
                .Select( d => d.Definition );

            snapshotDependencies = snapshotDependencies.Concat(
                parametrizedDeploymentDependencies
                    .Union( product.SourceDependencies )
                    .Where( d => d is { GenerateSnapshotDependency: true, CiConfiguration.DeploymentBuildType: not null } )
                    .Select( d => new TeamCitySnapshotDependency( d.CiConfiguration.DeploymentBuildType!, true ) ) );
        }

        // The standalone deployment doesn't expect pre-publishing and post-publishing step to be triggered,
        // so it's done from the develop branch.
        var teamCityDeploymentConfiguration = new TeamCityBuildConfiguration(
            objectName: isStandalone ? $"{configurationProperties.Configuration}DeploymentNoDependency" : $"{configurationProperties.Configuration}Deployment",
            name: (isStandalone ? "Standalone " : "") + (configurationProperties.BuildConfigurationInfo.TeamCityDeploymentName
                                                         ?? $"Deploy [{configurationProperties.Configuration}]"),
            productProperties.DefaultBranch,
            productProperties.VcsId,
            buildAgentRequirements: product.ResolvedBuildAgentRequirements )
        {
            BuildSteps = [step],
            IsDeployment = true,
            SnapshotDependencies = snapshotDependencies.OrderBy( d => d.ObjectId ).ToArray(),
            IsSshAgentRequired = productProperties.IsRepoRemoteSsh
        };

        return teamCityDeploymentConfiguration;
    }

    private static TeamCityBuildConfiguration CreateSshDeployConfiguration(
        ProductProperties productProperties,
        ConfigurationProperties configurationProperties,
        TeamCityBuildConfiguration teamCityBuildConfiguration,
        string deployedArtifactRules,
        SshPublisher[] sshPublishers )
    {
        var product = productProperties.Product;

        // The SSH Agent build feature loads a single key, so every target of this configuration must use the same one.
        var sshKeyName = sshPublishers[0].SshKeyName;

        if ( sshPublishers.Any( d => d.SshKeyName != sshKeyName ) )
        {
            throw new InvalidOperationException(
                $"All SshPublishers of the '{configurationProperties.Configuration}' configuration must use the same "
                + "SshKeyName, because the TeamCity SSH Agent build feature can load only one key." );
        }

        var steps = new List<BuildStep>();

        for ( var i = 0; i < sshPublishers.Length; i++ )
        {
            var deployment = sshPublishers[i];
            var sourcePath = $"{configurationProperties.PrivateArtifactsDirectory}/{deployment.ArchivePattern}";

            var bootstrapperCommand = deployment.BootstrapperCommand
                                      ?? GetDefaultBootstrapperCommand( deployment.RemoteDirectory, deployment.ArchivePattern );

            steps.Add(
                new SshUploadBuildStep(
                    $"ScpUpload_{i}",
                    $"SCP upload to {deployment.HostName}",
                    sourcePath,
                    $"{deployment.HostName}:{deployment.RemoteDirectory}",
                    deployment.UserName,
                    deployment.Port ) );

            steps.Add(
                new SshExecBuildStep(
                    $"SshExec_{i}",
                    $"Bootstrap on {deployment.HostName}",
                    bootstrapperCommand,
                    deployment.HostName,
                    deployment.UserName,
                    deployment.Port ) );
        }

        // Depend on the Build configuration so its artifacts (including the .zip) are downloaded onto the deploy agent.
        var snapshotDependencies = configurationProperties.SnapshotDependenciesForBuildConfiguration
            .Where( d => d.ArtifactRules != null )
            .Concat( [new TeamCitySnapshotDependency( teamCityBuildConfiguration.ObjectName, false, deployedArtifactRules )] );

        var sshDeploymentConfiguration = new TeamCityBuildConfiguration(
            objectName: $"{configurationProperties.Configuration}SshDeployment",
            name: $"Deploy via SSH [{configurationProperties.Configuration}]",
            productProperties.DefaultBranch,
            productProperties.VcsId,
            buildAgentRequirements: product.ResolvedBuildAgentRequirements )
        {
            BuildSteps = steps.ToArray(),
            IsDeployment = true,
            SnapshotDependencies = snapshotDependencies.OrderBy( d => d.ObjectId ).ToArray(),
            IsSshAgentRequired = true,
            SshAgentKeyName = sshKeyName
        };

        return sshDeploymentConfiguration;
    }

    /// <summary>
    /// Builds the default remote bootstrapper command: a PowerShell script that extracts the most recently uploaded
    /// archive matching <paramref name="archivePattern"/> from <paramref name="remoteDirectory"/> into a
    /// <c>current</c> subdirectory and runs the <c>deploy.ps1</c> it contains.
    /// </summary>
    /// <remarks>
    /// The SSH Exec runner runs this command through the target's default SSH shell. When that shell is PowerShell
    /// (the recommended setup), a plain <c>pwsh -Command "…"</c> would have its <c>$</c> variables expanded by that
    /// outer shell before the inner script runs. Emitting the script as a base64 <c>-EncodedCommand</c> — whose
    /// payload is only <c>[A-Za-z0-9+/=]</c>, with no <c>$</c>, quotes, or spaces — makes it pass through the outer
    /// shell (pwsh, bash, or cmd) unchanged. The base64 encodes the UTF-16LE bytes of the script, as
    /// <c>-EncodedCommand</c> requires.
    /// </remarks>
    internal static string GetDefaultBootstrapperCommand( string remoteDirectory, string archivePattern )
    {
        var script =
            "$ErrorActionPreference = 'Stop'; "
            + $"$directory = '{remoteDirectory}'; "
            + $"$archive = Get-ChildItem -LiteralPath $directory -Filter '{archivePattern}' | Sort-Object LastWriteTime | Select-Object -Last 1; "
            + "if (-not $archive) { throw \"No archive found in $directory.\" }; "
            + "$destination = Join-Path $directory 'current'; "
            + "if (Test-Path $destination) { Remove-Item -Recurse -Force $destination }; "
            + "Expand-Archive -LiteralPath $archive.FullName -DestinationPath $destination -Force; "
            + "& (Join-Path $destination 'deploy.ps1')";

        var encodedCommand = Convert.ToBase64String( System.Text.Encoding.Unicode.GetBytes( script ) );

        return $"pwsh -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}";
    }

    private static TeamCityBuildConfiguration CreateUpstreamMergeConfiguration( ProductProperties productProperties )
    {
        var product = productProperties.Product;

        // Use Claude Dockerfile for upstream merge to enable AI-assisted conflict resolution
        var claudeDockerSpec = product.DockerSpec?.WithClaudeDockerfile( product.EngineeringDirectory );

        // Dependencies on UpstreamMerge of dependent repos (for cascading merge order).
        // Only consolidated products have snapshot dependencies - normal products merge independently.
        IEnumerable<TeamCitySnapshotDependency> snapshotDependencies;

        if ( product.DependencyDefinition.IsConsolidated )
        {
            // For consolidated products, include both ParametrizedDependencies and SourceDependencies,
            // deduplicated by build type.
            snapshotDependencies =
                product.ParametrizedDependencies
                    .Where( d => d.ArtifactPickup == Dependencies.Model.DependencyArtifactPickup.Snapshot
                                 && d.Definition.GenerateSnapshotDependency
                                 && d.Definition.ProductFamily.UpstreamProductFamily != null )
                    .Select( d => d.Definition )
                    .Concat( product.SourceDependencies.Where( d => d.GenerateSnapshotDependency && d.ProductFamily.UpstreamProductFamily != null ) )
                    .DistinctBy( d => d.CiConfiguration.UpstreamMergeBuildType )
                    .Select( d => new TeamCitySnapshotDependency(
                                 d.CiConfiguration.UpstreamMergeBuildType,
                                 true,
                                 FailureAction: FailureAction.AddProblem ) )
                    .OrderBy( d => d.ObjectId );
        }
        else
        {
            // Normal products have no snapshot dependencies - they merge independently.
            snapshotDependencies = [];
        }

        var upstreamMergeConfiguration = new TeamCityBuildConfiguration(
            "UpstreamMerge",
            "Upstream Merge",
            productProperties.DefaultBranch,
            productProperties.VcsId,
            product.ResolvedBuildAgentRequirements )
        {
            BuildSteps =
            [
                new EngineeringCommandBuildStep(
                    "UpstreamMerge",
                    "Merge upstream",
                    "upstream-merge",
                    areCustomArgumentsAllowed: true,
                    dockerSpec: claudeDockerSpec,
                    timeout: product.UpstreamMergeTimeout,
                    useSnapshot: true )
            ],
            SnapshotDependencies = snapshotDependencies.ToArray(),
            IsSshAgentRequired = productProperties.IsRepoRemoteSsh
        };

        return upstreamMergeConfiguration;
    }

    private static TeamCityBuildConfiguration CreateBumpConfiguration( ProductProperties productProperties )
    {
        var bumpConfiguration = new TeamCityBuildConfiguration(
            objectName: "VersionBump",
            name: $"Version Bump",
            productProperties.DefaultBranch,
            productProperties.VcsId,
            buildAgentRequirements: productProperties.Product.ResolvedBuildAgentRequirements )
        {
            BuildSteps =
            [
                new EngineeringCommandBuildStep(
                    "Bump",
                    "Bump",
                    "bump",
                    areCustomArgumentsAllowed: true,
                    dockerSpec: productProperties.Product.DockerSpec,
                    timeout: productProperties.Product.VersionBumpTimeout )
            ],
            IsSshAgentRequired = productProperties.IsRepoRemoteSsh
        };

        return bumpConfiguration;
    }

    private static TeamCityBuildConfiguration CreateSwapConfiguration(
        ProductProperties productProperties,
        ConfigurationProperties configurationProperties,
        TeamCityBuildConfiguration? teamCityDeploymentConfiguration,
        TeamCityBuildConfiguration teamCityBuildConfiguration,
        string deployedArtifactRule )
    {
        var snapshotDependencies = new List<TeamCitySnapshotDependency>();

        if ( teamCityDeploymentConfiguration != null )
        {
            snapshotDependencies.Add( new TeamCitySnapshotDependency( teamCityDeploymentConfiguration.ObjectName, false ) );
            snapshotDependencies.Add( new TeamCitySnapshotDependency( teamCityBuildConfiguration.ObjectName, false, deployedArtifactRule ) );
        }

        var swapConfiguration = new TeamCityBuildConfiguration(
            objectName: $"{configurationProperties.Configuration}Swap",
            name: configurationProperties.BuildConfigurationInfo.TeamCitySwapName ?? $"Swap [{configurationProperties.Configuration}]",
            productProperties.DeploymentBranch,
            productProperties.VcsId,
            buildAgentRequirements: productProperties.Product.ResolvedBuildAgentRequirements )
        {
            BuildSteps =
            [
                new EngineeringCommandBuildStep(
                    "Swap",
                    "Swap",
                    "swap",
                    $"--configuration {configurationProperties.Configuration}",
                    true,
                    productProperties.Product.DockerSpec,
                    configurationProperties.BuildConfigurationInfo.SwapTimeout ?? productProperties.Product.SwapTimeout )
            ],
            IsDeployment = true,
            SnapshotDependencies = snapshotDependencies.OrderBy( d => d.ObjectId ).ToArray()
        };

        return swapConfiguration;
    }

    private static TeamCityBuildConfiguration CreateBuildConfiguration(
        BuildContext context,
        ProductProperties productProperties,
        ConfigurationProperties configurationProperties,
        string publishedArtifactRules,
        ImmutableArray<string> additionalArtifactRules )
    {
        var product = productProperties.Product;
        var teamCityBuildSteps = new List<BuildStep>();

        if ( !product.UseDocker )
        {
            teamCityBuildSteps.Add( new EngineeringCommandBuildStep( "PreKill", "Kill background processes before cleanup", "tools kill" ) );
        }

        var requiresUpstreamCheck =

            // The check is required.
            configurationProperties.BuildConfigurationInfo.RequiresUpstreamCheck

            // There is upstream product to check.
            && product.ProductFamily.UpstreamProductFamily != null

            // For products with the release branch, the check is done as part of the deployment preparation step.
            && product.DependencyDefinition.ReleaseBranch == null;

        if ( requiresUpstreamCheck )
        {
            teamCityBuildSteps.Add(
                new EngineeringCommandBuildStep(
                    "UpstreamCheck",
                    "Check pending upstream changes",
                    "tools git check-upstream",
                    areCustomArgumentsAllowed: true,
                    dockerSpec: product.DockerSpec ) );
        }

        teamCityBuildSteps.Add( new EngineeringBuildBuildStep( configurationProperties.Configuration, true, product.DockerSpec, context.BuildTimeout ) );

        if ( !product.UseDocker )
        {
            teamCityBuildSteps.Add( new EngineeringCommandBuildStep( "PostKill", "Kill background processes before next build", "tools kill" ) );
        }

        // The default branch for the public build cannot be set to the release branch,
        // because the scheduled build would not trigger the build on the develop branch
        // where the develop branch name differs.
        // Only the consolidated public build has the release branch as the default branch
        // and it expects that the release branch name is the same for each project.
        // If it happens that it's not, the build of the develop branch would be triggered
        // during the consolidated public build on such project, but the correct
        // one would be triggered during deployment.
        var teamCityBuildConfiguration = new TeamCityBuildConfiguration(
            $"{configurationProperties.Configuration}Build",
            configurationProperties.BuildConfigurationInfo.TeamCityBuildName ?? $"Build [{configurationProperties.Configuration}]",
            productProperties.DefaultBranch,
            productProperties.VcsId,
            product.ResolvedBuildAgentRequirements )
        {
            BuildSteps = teamCityBuildSteps.ToArray(),
            ArtifactRules = publishedArtifactRules,
            AdditionalArtifactRules = additionalArtifactRules.ToArray(),
            BuildTriggers = configurationProperties.BuildConfigurationInfo.BuildTriggers,
            SnapshotDependencies = configurationProperties.SnapshotDependenciesForBuildConfiguration,
            SourceDependencies = product.BuildRequiresSourceDependencies ? productProperties.SourceDependencies : [],
            IsSshAgentRequired = requiresUpstreamCheck && productProperties.IsRepoRemoteSsh,
            RequiresCommitStatusPublisher = true
        };

        return teamCityBuildConfiguration;
    }

    /// <summary>
    /// Gets the distinct, ordered set of package ID patterns (the <c>*</c> wildcard is allowed) to delete from the NuGet
    /// cache before each build: the packages produced by the <paramref name="product"/> itself, plus those produced by
    /// the whole closure of its dependencies, across all build configurations. These are the "namespace prefixes" used
    /// to clean the NuGet cache before each build.
    /// </summary>
    private static string[] GetNuGetCachePackagePrefixes( Product product )
    {
        var configurations = new[] { BuildConfiguration.Debug, BuildConfiguration.Release, BuildConfiguration.Public };

        var dependencyPatterns = configurations
            .SelectMany( c => product.DependencyDefinition.GetAllDependencies( c ) )
            .SelectMany( d => d.Definition.PackagePatterns );

        // Include the packages produced by the current repo itself, not just its dependencies, so that stale packages
        // from a previous build of this repo cannot leak into the build either.
        return product.DependencyDefinition.PackagePatterns
            .Concat( dependencyPatterns )
            .Distinct( StringComparer.OrdinalIgnoreCase )
            .OrderBy( p => p, StringComparer.OrdinalIgnoreCase )
            .ToArray();
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

    private static void GenerateTeamCityConfiguration( BuildContext context, TeamCityProject project )
    {
        var content = new StringWriter();
        project.GenerateTeamcityCode( content );

        var filePath = Path.Combine( context.RepoDirectory, ".teamcity", "settings.kts" );

        TextFileHelper.WriteIfDifferent( filePath, content.ToString(), context );
    }
}