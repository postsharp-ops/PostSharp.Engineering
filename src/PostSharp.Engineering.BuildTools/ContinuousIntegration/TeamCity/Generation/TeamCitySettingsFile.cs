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

        // Root-level build configurations of the generated TeamCity project.
        var teamCityBuildConfigurations = new List<TeamCityBuildConfiguration>();

        // Per-build-configuration deployment sub-projects (folders), each holding that configuration's deployments,
        // swaps, and their Deploy All / Swap All aggregates.
        var subProjects = new List<TeamCityProject>();

        // Every build configuration that lives inside a sub-project, tracked flat so that the product-wide
        // post-processing (NuGet cache cleanup, GitHub App token) can be applied to them as well.
        var subProjectConfigurations = new List<TeamCityBuildConfiguration>();

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

            // Group publishers into deployments by their effective deployment name. Each group becomes its own TeamCity
            // deployment configuration. Publishers without an explicit DeploymentName join the "default" group, while SSH
            // publishers join the "ssh" group by default. SSH publishers are inert markers deployed by native TeamCity
            // SSH runners; non-SSH publishers deploy through the 'b publish' step.
            var allPublishers = ( configurationInfo.PublicPublishers ?? [] ).Concat( configurationInfo.PrivatePublishers ?? [] ).ToList();

            var deploymentGroups = allPublishers
                .GroupBy( p => p.EffectiveDeploymentName, StringComparer.Ordinal )
                .OrderBy( g => g.Key, StringComparer.Ordinal )
                .ToList();

            // One "primary" deployment configuration per group (the target of Deploy All and of same-named swappers).
            var primaryDeploymentConfigurations = new List<TeamCityBuildConfiguration>();

            // Every deployment configuration of this build configuration, including the standalone variant of default.
            var deploymentConfigurationsForConfig = new List<TeamCityBuildConfiguration>();

            // Maps a deployment name to its primary configuration, so that swappers can depend on the deployment they swap.
            var deploymentConfigurationsByName = new Dictionary<string, TeamCityBuildConfiguration>( StringComparer.Ordinal );

            foreach ( var group in deploymentGroups )
            {
                var deploymentName = group.Key;
                var sshPublishers = group.OfType<SshPublisher>().ToArray();
                var hasPublishStepPublisher = group.Any( p => !p.IsInertAtPublishTime );

                // Publishers that are not inert deploy through a 'b publish' step, which is only exported when the
                // configuration opts in.
                var includePublishStep = hasPublishStepPublisher && configurationInfo.ExportsToTeamCityDeploy;

                if ( !includePublishStep && sshPublishers.Length == 0 )
                {
                    // A publish-only group whose 'b publish' deploy is not exported produces no configuration.
                    continue;
                }

                var deploymentConfiguration = CreateDeploymentConfiguration(
                    productProperties,
                    configurationProperties,
                    teamCityBuildConfiguration,
                    deployedArtifactRules,
                    deploymentName,
                    isStandalone: false,
                    includePublishStep,
                    sshPublishers,
                    includeCrossDependencies: deploymentName == "default" );

                primaryDeploymentConfigurations.Add( deploymentConfiguration );
                deploymentConfigurationsForConfig.Add( deploymentConfiguration );
                deploymentConfigurationsByName[deploymentName] = deploymentConfiguration;
            }

            // The default group additionally exports a standalone (no-dependency) variant when requested. It is an
            // alternative way to run the default deployment, not a separate target, so it is not part of Deploy All.
            var defaultGroup = deploymentGroups.FirstOrDefault( g => g.Key == "default" );

            if ( configurationInfo.ExportsToTeamCityDeployWithoutDependencies
                 && defaultGroup != null
                 && defaultGroup.Any( p => !p.IsInertAtPublishTime ) )
            {
                var standaloneConfiguration = CreateDeploymentConfiguration(
                    productProperties,
                    configurationProperties,
                    teamCityBuildConfiguration,
                    deployedArtifactRules,
                    "default",
                    isStandalone: true,
                    includePublishStep: true,
                    sshPublishers: [],
                    includeCrossDependencies: false );

                deploymentConfigurationsForConfig.Add( standaloneConfiguration );
            }

            // Group swappers into swap configurations by their deployment name (unless swapping happens right after
            // publishing, in which case there is no separate swap configuration).
            var swapConfigurationsForConfig = new List<TeamCityBuildConfiguration>();

            if ( configurationInfo is { Swappers: { Length: > 0 }, SwapAfterPublishing: false } )
            {
                var swapGroups = configurationInfo.Swappers
                    .GroupBy( s => s.EffectiveDeploymentName, StringComparer.Ordinal )
                    .OrderBy( g => g.Key, StringComparer.Ordinal );

                foreach ( var swapGroup in swapGroups )
                {
                    deploymentConfigurationsByName.TryGetValue( swapGroup.Key, out var matchingDeployment );

                    if ( matchingDeployment == null )
                    {
                        context.Console.WriteWarning(
                            $"The '{swapGroup.Key}' swapper group of the '{configurationProperties.Configuration}' configuration has no "
                            + "matching deployment, so its swap configuration will have no deployment dependency." );
                    }

                    var swapConfiguration = CreateSwapConfiguration(
                        productProperties,
                        configurationProperties,
                        swapGroup.Key,
                        matchingDeployment,
                        teamCityBuildConfiguration,
                        deployedArtifactRules );

                    swapConfigurationsForConfig.Add( swapConfiguration );
                }
            }

            // A Deploy All / Swap All aggregate is generated as soon as there are multiple deployments / swaps. When
            // either exists, all deployments and swaps of this build configuration are moved into a sub-project folder.
            var deployAll = primaryDeploymentConfigurations.Count >= 2
                ? CreateDeployAllConfiguration( productProperties, configurationProperties, primaryDeploymentConfigurations )
                : null;

            var swapAll = swapConfigurationsForConfig.Count >= 2
                ? CreateSwapAllConfiguration( productProperties, configurationProperties, swapConfigurationsForConfig )
                : null;

            if ( deployAll != null || swapAll != null )
            {
                var folderConfigurations = new List<TeamCityBuildConfiguration>();

                if ( deployAll != null )
                {
                    folderConfigurations.Add( deployAll );
                }

                folderConfigurations.AddRange( deploymentConfigurationsForConfig );

                if ( swapAll != null )
                {
                    folderConfigurations.Add( swapAll );
                }

                folderConfigurations.AddRange( swapConfigurationsForConfig );

                subProjectConfigurations.AddRange( folderConfigurations );

                subProjects.Add(
                    new TeamCityProject(
                        $"{configurationProperties.Configuration}Deployments",
                        $"Deployments [{configurationProperties.Configuration}]",
                        folderConfigurations.ToArray(),
                        [] ) );
            }
            else
            {
                // A single deployment (and/or a single swap): keep the historical flat, top-level layout.
                teamCityBuildConfigurations.AddRange( deploymentConfigurationsForConfig );
                teamCityBuildConfigurations.AddRange( swapConfigurationsForConfig );
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

        // Add product-defined. Those naming a project folder are grouped into a sub-project of that name, so that
        // a product with dozens of test cells does not present them as one flat list; the rest sit at the root.
        var folderedConfigurations = new Dictionary<string, List<TeamCityBuildConfiguration>>( StringComparer.Ordinal );

        foreach ( var additional in product.AdditionalCiBuildConfigurations )
        {
            var configuration = additional.TeamCityBuildConfiguration( productProperties, teamCityBuildBuildConfigurations );
            configuration.GitHubAppTokenOverride = additional.GitHubAppToken;

            if ( additional.ProjectFolder == null )
            {
                teamCityBuildConfigurations.Add( configuration );
            }
            else
            {
                if ( !folderedConfigurations.TryGetValue( additional.ProjectFolder, out var configurationsInFolder ) )
                {
                    configurationsInFolder = [];
                    folderedConfigurations.Add( additional.ProjectFolder, configurationsInFolder );
                }

                configurationsInFolder.Add( configuration );
            }
        }

        foreach ( var folder in folderedConfigurations )
        {
            // The object name is the folder name with everything a Kotlin identifier cannot carry removed.
            var objectName = new string( folder.Key.Where( char.IsLetterOrDigit ).ToArray() );

            subProjects.Add( new TeamCityProject( objectName, folder.Key, folder.Value.ToArray(), [] ) );
            subProjectConfigurations.AddRange( folder.Value );
        }

        // Add from extensions.
        foreach ( var extension in product.Extensions )
        {
            if ( !extension.AddTeamcityBuildConfiguration( context, teamCityBuildConfigurations ) )
            {
                return false;
            }
        }

        // Post-processing must reach every generated configuration, including those nested in deployment sub-projects.
        var allConfigurations = teamCityBuildConfigurations.Concat( subProjectConfigurations ).ToList();

        // Insert, in front of every build configuration, a step that cleans the NuGet cache of all packages produced by
        // the current repo and by the whole closure of its dependencies, so stale packages cannot leak into the build.
        var nugetCachePackagePrefixes = GetNuGetCachePackagePrefixes( product );

        if ( nugetCachePackagePrefixes.Length > 0 )
        {
            foreach ( var teamCityBuildConfiguration in allConfigurations )
            {
                teamCityBuildConfiguration.NuGetCachePackagePrefixes = nugetCachePackagePrefixes;
            }
        }

        // A GitHub App has no long-lived credential, so every build configuration issues its own installation token.
        if ( product.DependencyDefinition.VcsRepository is GitHubRepository gitHubRepository
             && product.DependencyDefinition.EffectiveGitHubAppConnectionId is { } gitHubAppConnectionId )
        {
            foreach ( var teamCityBuildConfiguration in allConfigurations )
            {
                teamCityBuildConfiguration.GitHubAppBuildScopedToken = CreateBuildScopedTokenSettings(
                    context.Console,
                    gitHubRepository,
                    gitHubAppConnectionId,
                    teamCityBuildConfiguration,
                    product.AdditionalGitHubTokenRepositories );
            }
        }

        var teamCityProject = new TeamCityProject( teamCityBuildConfigurations.ToArray(), [], subProjects.ToArray() );

        GeneratePom( context, product.DependencyDefinition.CiConfiguration.ProjectId.Id, product.DependencyDefinition.CiConfiguration.BaseUrl );
        GenerateTeamCityConfiguration( context, teamCityProject );

        return true;
    }

    /// <summary>
    /// Creates the build-scoped token settings of a single build configuration. The connection and the parameter come
    /// from <see cref="TeamCityBuildConfiguration.GitHubAppTokenOverride"/> when the build configuration acts under an
    /// identity of its own, and from the repository otherwise. A build configuration issues exactly one token, so an
    /// override substitutes for the repository's connection instead of adding a second token.
    /// </summary>
    /// <remarks>
    /// The scope of the token is deliberately computed from <paramref name="connectionId"/>, the connection of the
    /// repository, and never from the override. <see cref="GetTargetRepositories"/> uses the connection as a proxy for
    /// the GitHub organization, and an overriding connection serves the same organization as the repository, so passing
    /// it would match no source dependency, warn about each one, and silently narrow the token to the owning repository.
    /// </remarks>
    internal static GitHubAppBuildScopedTokenSettings CreateBuildScopedTokenSettings(
        ConsoleHelper console,
        GitHubRepository repository,
        string connectionId,
        TeamCityBuildConfiguration buildConfiguration,
        ImmutableArray<GitHubRepository> additionalRepositories )
    {
        var tokenOverride = buildConfiguration.GitHubAppTokenOverride;

        return new GitHubAppBuildScopedTokenSettings(
            tokenOverride?.ConnectionId ?? connectionId,
            GetTargetRepositories( console, repository, connectionId, buildConfiguration, additionalRepositories ),
            tokenOverride?.EffectiveParameterName ?? GitHubAppBuildScopedTokenSettings.DefaultParameterName );
    }

    /// <summary>
    /// Gets the repositories that the build-scoped token of <paramref name="buildConfiguration"/> must reach: the
    /// repository of the product itself, the repository of every source dependency the configuration checks out (because
    /// commands such as <c>bump</c> walk into the source dependencies and push to them), and the
    /// <paramref name="additionalRepositories"/> the product pushes to without checking them out. Deriving the list from
    /// the owning repository alone leaves those pushes with a token that has no access to their target, which GitHub
    /// rejects with a 403.
    /// </summary>
    /// <remarks>
    /// A token is issued by a single GitHub App connection, and a connection only serves the repositories of its own
    /// organization. A repository of another organization therefore cannot be covered by this token. For a source
    /// dependency that is legitimate — the build only reads it, and the checkout authenticates through the VCS root of
    /// the dependency, not through this token — so it is skipped with a warning instead of failing the generation. An
    /// additional repository of another organization is a configuration mistake and is likewise skipped with a warning.
    /// </remarks>
    internal static ImmutableArray<string> GetTargetRepositories(
        ConsoleHelper console,
        GitHubRepository repository,
        string connectionId,
        TeamCityBuildConfiguration buildConfiguration,
        ImmutableArray<GitHubRepository> additionalRepositories )
    {
        var targetRepositories = ImmutableArray.CreateBuilder<string>();
        targetRepositories.Add( repository.Name );

        void AddRepository( string name )
        {
            if ( !targetRepositories.Contains( name ) )
            {
                targetRepositories.Add( name );
            }
        }

        foreach ( var sourceDependency in buildConfiguration.SourceDependencies ?? [] )
        {
            var definition = sourceDependency.Definition;

            if ( definition.VcsRepository is not GitHubRepository sourceRepository )
            {
                // Not hosted on GitHub, so no GitHub App token can reach it anyway.
                continue;
            }

            if ( definition.EffectiveGitHubAppConnectionId != connectionId )
            {
                console.WriteWarning(
                    $"The '{buildConfiguration.Name}' build configuration checks out '{definition.Name}', which is served by the "
                    + $"'{definition.EffectiveGitHubAppConnectionId ?? "(none)"}' GitHub App connection, while the build issues its token from "
                    + $"'{connectionId}'. A token cannot reach the repositories of another organization, so the build will not be able to "
                    + $"push to '{sourceRepository.Owner}/{sourceRepository.Name}'." );

                continue;
            }

            AddRepository( sourceRepository.Name );
        }

        foreach ( var additionalRepository in additionalRepositories )
        {
            // The connection is bound to one organization, so an additional repository is reachable exactly when it
            // shares the organization of the product repository.
            if ( !string.Equals( additionalRepository.Owner, repository.Owner, StringComparison.OrdinalIgnoreCase ) )
            {
                console.WriteWarning(
                    $"The '{repository.Name}' product lists '{additionalRepository.Owner}/{additionalRepository.Name}' among its additional "
                    + $"token repositories, but it belongs to a different organization than the product repository "
                    + $"'{repository.Owner}/{repository.Name}'. A build-scoped token is issued by a single connection and cannot reach the "
                    + $"repositories of another organization, so it is left out of the token of the '{buildConfiguration.Name}' build configuration." );

                continue;
            }

            AddRepository( additionalRepository.Name );
        }

        return targetRepositories.ToImmutable();
    }

    /// <summary>
    /// Creates the deployment configuration of a single deployment group. Non-SSH publishers of the group (when
    /// <paramref name="includePublishStep"/> is set) deploy through a <c>b publish --deployment</c> step;
    /// <paramref name="sshPublishers"/> deploy through native TeamCity SSH upload/exec runners. The two kinds can
    /// coexist in one group.
    /// </summary>
    private static TeamCityBuildConfiguration CreateDeploymentConfiguration(
        ProductProperties productProperties,
        ConfigurationProperties configurationProperties,
        TeamCityBuildConfiguration teamCityBuildConfiguration,
        string deployedArtifactRules,
        string deploymentName,
        bool isStandalone,
        bool includePublishStep,
        SshPublisher[] sshPublishers,
        bool includeCrossDependencies )
    {
        var product = productProperties.Product;
        var configurationInfo = configurationProperties.BuildConfigurationInfo;
        var steps = new List<BuildStep>();

        if ( includePublishStep )
        {
            steps.Add(
                new EngineeringCommandBuildStep(
                    "Publish",
                    "Publish",
                    "publish",
                    $"--configuration {configurationProperties.Configuration} --deployment {deploymentName}{(isStandalone ? " --standalone" : "")}",
                    true,
                    product.DockerSpec,
                    configurationInfo.DeploymentTimeout ?? product.DeploymentTimeout ) );
        }

        // The SSH Agent build feature loads a single key, so every SSH target of this group must use the same one.
        string? sshKeyName = null;

        if ( sshPublishers.Length > 0 )
        {
            sshKeyName = sshPublishers[0].SshKeyName;

            if ( sshPublishers.Any( d => d.SshKeyName != sshKeyName ) )
            {
                throw new InvalidOperationException(
                    $"All SshPublishers of the '{deploymentName}' deployment of the '{configurationProperties.Configuration}' configuration "
                    + "must use the same SshKeyName, because the TeamCity SSH Agent build feature can load only one key." );
            }

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
        }

        // Depend on the Build configuration so its artifacts (including the .zip) are downloaded onto the deploy agent.
        var snapshotDependencies = configurationProperties.SnapshotDependenciesForBuildConfiguration
            .Where( d => d.ArtifactRules != null )
            .Concat( [new TeamCitySnapshotDependency( teamCityBuildConfiguration.ObjectName, false, deployedArtifactRules )] );

        // Only the primary default deployment carries the cross-product deployment dependencies; the standalone variant
        // and additional named deployments depend on the Build configuration alone.
        if ( includeCrossDependencies && !isStandalone )
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

        var (objectName, name) = GetDeploymentNaming( configurationProperties, deploymentName, isStandalone );

        return new TeamCityBuildConfiguration(
            objectName,
            name,
            productProperties.DefaultBranch,
            productProperties.VcsId,
            buildAgentRequirements: product.ResolvedBuildAgentRequirements )
        {
            BuildSteps = steps.ToArray(),
            IsDeployment = true,
            SnapshotDependencies = snapshotDependencies.OrderBy( d => d.ObjectId ).ToArray(),

            // An SSH group loads its own key; a publish-only group loads the conventional key when the repo uses Git over SSH.
            IsSshAgentRequired = sshPublishers.Length > 0 || productProperties.IsRepoRemoteSsh,
            SshAgentKeyName = sshKeyName
        };
    }

    /// <summary>
    /// Computes the object name and display name of a deployment configuration. The <c>default</c> and <c>ssh</c>
    /// deployments keep their historical names for backward compatibility; other deployments are suffixed with a
    /// Kotlin-safe form of their name.
    /// </summary>
    private static (string ObjectName, string Name) GetDeploymentNaming(
        ConfigurationProperties configurationProperties,
        string deploymentName,
        bool isStandalone )
    {
        var configuration = configurationProperties.Configuration;
        var configurationInfo = configurationProperties.BuildConfigurationInfo;

        if ( deploymentName == "default" )
        {
            var baseName = configurationInfo.TeamCityDeploymentName ?? $"Deploy [{configuration}]";

            return isStandalone
                ? ($"{configuration}DeploymentNoDependency", $"Standalone {baseName}")
                : ($"{configuration}Deployment", baseName);
        }

        if ( deploymentName == "ssh" )
        {
            return ($"{configuration}SshDeployment", $"Deploy via SSH [{configuration}]");
        }

        return ($"{configuration}Deployment_{ToObjectNameSuffix( deploymentName )}", $"Deploy {deploymentName} [{configuration}]");
    }

    /// <summary>
    /// Converts a free-form deployment name into a Kotlin-safe PascalCase identifier suffix (e.g. <c>web-staging</c>
    /// becomes <c>WebStaging</c>). The result is always appended to a name that already starts with a letter, so a
    /// suffix starting with a digit is harmless.
    /// </summary>
    internal static string ToObjectNameSuffix( string deploymentName )
    {
        var parts = System.Text.RegularExpressions.Regex.Split( deploymentName, "[^A-Za-z0-9]+" )
            .Where( p => p.Length > 0 )
            .Select( p => char.ToUpperInvariant( p[0] ) + p.Substring( 1 ) );

        var suffix = string.Concat( parts );

        return suffix.Length > 0 ? suffix : "X";
    }

    /// <summary>
    /// Creates the composite <c>Deploy All</c> configuration that aggregates every deployment of a build configuration,
    /// so that triggering it deploys all targets at once.
    /// </summary>
    private static TeamCityBuildConfiguration CreateDeployAllConfiguration(
        ProductProperties productProperties,
        ConfigurationProperties configurationProperties,
        IReadOnlyList<TeamCityBuildConfiguration> deploymentConfigurations )
    {
        var snapshotDependencies = deploymentConfigurations
            .Select( d => new TeamCitySnapshotDependency( d.ObjectName, false ) )
            .OrderBy( d => d.ObjectId )
            .ToArray();

        // A null BuildAgentRequirements makes the configuration composite (Type.COMPOSITE).
        return new TeamCityBuildConfiguration(
            $"{configurationProperties.Configuration}DeployAll",
            $"Deploy All [{configurationProperties.Configuration}]",
            productProperties.DefaultBranch,
            productProperties.VcsId )
        {
            BuildSteps = [],
            SnapshotDependencies = snapshotDependencies
        };
    }

    /// <summary>
    /// Creates the composite <c>Swap All</c> configuration that aggregates every swap of a build configuration.
    /// </summary>
    private static TeamCityBuildConfiguration CreateSwapAllConfiguration(
        ProductProperties productProperties,
        ConfigurationProperties configurationProperties,
        IReadOnlyList<TeamCityBuildConfiguration> swapConfigurations )
    {
        var snapshotDependencies = swapConfigurations
            .Select( d => new TeamCitySnapshotDependency( d.ObjectName, false ) )
            .OrderBy( d => d.ObjectId )
            .ToArray();

        return new TeamCityBuildConfiguration(
            $"{configurationProperties.Configuration}SwapAll",
            $"Swap All [{configurationProperties.Configuration}]",
            productProperties.DeploymentBranch,
            productProperties.VcsId )
        {
            BuildSteps = [],
            SnapshotDependencies = snapshotDependencies
        };
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
        string deploymentName,
        TeamCityBuildConfiguration? matchingDeploymentConfiguration,
        TeamCityBuildConfiguration teamCityBuildConfiguration,
        string deployedArtifactRule )
    {
        var configuration = configurationProperties.Configuration;
        var configurationInfo = configurationProperties.BuildConfigurationInfo;
        var snapshotDependencies = new List<TeamCitySnapshotDependency>();

        // Link the swap to the deployment it swaps: depend on the same-named deployment and pull the Build artifacts.
        if ( matchingDeploymentConfiguration != null )
        {
            snapshotDependencies.Add( new TeamCitySnapshotDependency( matchingDeploymentConfiguration.ObjectName, false ) );
            snapshotDependencies.Add( new TeamCitySnapshotDependency( teamCityBuildConfiguration.ObjectName, false, deployedArtifactRule ) );
        }

        var (objectName, name) = deploymentName == "default"
            ? ($"{configuration}Swap", configurationInfo.TeamCitySwapName ?? $"Swap [{configuration}]")
            : ($"{configuration}Swap_{ToObjectNameSuffix( deploymentName )}", $"Swap {deploymentName} [{configuration}]");

        var swapConfiguration = new TeamCityBuildConfiguration(
            objectName,
            name,
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
                    $"--configuration {configuration} --deployment {deploymentName}",
                    true,
                    productProperties.Product.DockerSpec,
                    configurationInfo.SwapTimeout ?? productProperties.Product.SwapTimeout )
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