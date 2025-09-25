// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.VisualStudio.Services.Common;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Triggers;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.Arguments;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Files;

internal static class TeamCitySettingsFile
{
    internal static bool TryWrite( BuildContext context, CommonCommandSettings settings )
        => context.Product.IsBundle ? TryWriteConsolidated( context ) : TryWriteStandalone( context, settings );

    private static bool TryWriteStandalone( BuildContext context, CommonCommandSettings settings )
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
                context.Product.PublicArtifactsDirectory.Replace( "\\", "/", StringComparison.Ordinal );

            var privateArtifactsDirectory =
                context.Product.GetPrivateArtifactsDirectory( configuration ).Replace( "\\", "/", StringComparison.Ordinal );

            var testResultsDirectory =
                context.Product.TestResultsDirectory.Replace( "\\", "/", StringComparison.Ordinal );

            var logsDirectory = context.Product.LogsDirectory.Replace( "\\", "/", StringComparison.Ordinal );
            var dumpsDirectory = context.Product.DumpDirectory.Replace( "\\", "/", StringComparison.Ordinal );

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

            teamCityBuildSteps.Add( new TeamCityEngineeringBuildBuildStep( configuration, true, product.DockerSpec, product.BuildTimeoutPlusMargin ) );

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
                        product.DockerSpec );

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
                        BuildTimeOutThreshold = configurationInfo.DeploymentTimeOutThreshold ?? product.DeploymentTimeout,
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
                        BuildTimeOutThreshold = configurationInfo.DeploymentTimeOutThreshold ?? product.DeploymentTimeout,
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
                            new TeamCityEngineeringCommandBuildStep( "Swap", "Swap", "swap", $"--configuration {configuration}", true, product.DockerSpec )
                        ],
                        IsDeployment = true,
                        SnapshotDependencies = swapDependencies.OrderBy( d => d.ObjectId ).ToArray(),
                        BuildTimeOutThreshold = configurationInfo.SwapTimeOutThreshold ?? product.SwapTimeout
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
                            new TeamCityEngineeringCommandBuildStep( "Bump", "Bump", "bump", areCustomArgumentsAllowed: true, dockerSpec: product.DockerSpec )
                        ],
                        BuildTimeOutThreshold = product.VersionBumpTimeout,
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
                            dockerSpec: product.DockerSpec )
                    ],
                    SnapshotDependencies = snapshotDependencies,
                    BuildTriggers = [new SourceBuildTrigger()],
                    BuildTimeOutThreshold = product.DownstreamMergeTimeout,
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

    private static bool TryWriteConsolidated( BuildContext context )
    {
        // This method is implemented so it preserves the order of all entities in the resulting script.

        if ( !TeamCityHelper.TryConnectTeamCity( context, out var tc ) )
        {
            return false;
        }

        var product = context.Product;
        var consolidatedProjectId = product.DependencyDefinition.CiConfiguration.ProjectId;
        var consolidatedProjectIdPrefix = $"{consolidatedProjectId}_";
        var defaultBranch = product.DependencyDefinition.Branch;
        var deploymentBranch = product.DependencyDefinition.ReleaseBranch;
        var defaultBranchParameter = product.DependencyDefinition.VcsRepository.DefaultBranchParameter;
        var vcsRootId = TeamCityHelper.GetVcsRootId( product.DependencyDefinition );

        if ( deploymentBranch == null )
        {
            context.Console.WriteError( $"Release branch not set for the consolidated project." );

            return false;
        }

        var tcConfigurations = new List<TeamCityBuildConfiguration>();
        var nuGetConfigurations = new List<TeamCityBuildConfiguration>();

        if ( !tc.TryGetOrderedSubprojectsRecursively(
                context.Console,
                consolidatedProjectId.ParentId,
                out var subprojects ) )
        {
            return false;
        }

        var consolidatedProjectName = product.ProductName;

        var buildConfigurations = new List<(string ProjectId, string ProjectName, string BuildConfigurationId, HashSet<string> SnapshotDependencies)>();

        var buildConfigurationsById =
            new Dictionary<string, (string ProjectId, string ProjectName, string BuildConfigurationId, HashSet<string> SnapshotDependencies)>();

        var buildConfigurationsByKind =
            new Dictionary<string, List<(string ProjectId, string ProjectName, string BuildConfigurationId, HashSet<string> SnapshotDependencies)>>();

        // Exclude the consolidated build project.
        subprojects = subprojects.Where( p => !p.Id.EndsWith( $"_{consolidatedProjectName}", StringComparison.Ordinal ) ).ToImmutableArray();

        foreach ( var project in subprojects )
        {
            if ( !tc.TryGetProjectsBuildConfigurations( context.Console, project.Id, out var projectsBuildConfigurations ) )
            {
                return false;
            }

            foreach ( var buildConfigurationId in projectsBuildConfigurations )
            {
                if ( !tc.TryGetBuildConfigurationsSnapshotDependencies( context.Console, buildConfigurationId, out var snapshotDependencies ) )
                {
                    return false;
                }

                var buildConfigurationKind = buildConfigurationId.Split( '_' ).Last();

                if ( !buildConfigurationsByKind.TryGetValue( buildConfigurationKind, out var buildConfigurationsOfKind ) )
                {
                    buildConfigurationsOfKind =
                        new List<(string ProjectId, string ProjectName, string BuildConfigurationId, HashSet<string> SnapshotDependencies)>();

                    buildConfigurationsByKind.Add( buildConfigurationKind, buildConfigurationsOfKind );
                }

                var buildConfiguration = (project.Id, project.Name, buildConfigurationId, snapshotDependencies.Value.ToHashSet());
                buildConfigurations.Add( buildConfiguration );
                buildConfigurationsOfKind.Add( buildConfiguration );
                buildConfigurationsById.Add( buildConfigurationId, buildConfiguration );
            }
        }

        // TeamCity doesn't allow to have artifact dependencies that match no artifacts.
        // TODO: Make this configurable.
        string[] projectsWithNoNuGetArtifacts = [".Vsx", ".Documentation", ".Try", ".Tests."];

        static string MarkNuGetObjectId( string objectId ) => $"NuGet{objectId}";

        bool TryPopulateBuildConfigurations(
            BuildConfiguration configuration,
            string consolidatedBuildObjectName,
            string consolidatedBuildConfigurationName,
            IBuildTrigger[] consolidatedBuildTriggers,
            string nuGetBuildObjectName,
            string nuGetBuildConfigurationName,
            [NotNullWhen( true )] out TeamCityBuildConfiguration? consolidatedBuildConfiguration,
            [NotNullWhen( true )] out TeamCityBuildConfiguration? nuGetBuildConfiguration,
            out Dictionary<string, HashSet<string>> dependenciesByProjectId,
            out List<TeamCitySnapshotDependency> nuGetDependencies,
            [NotNullWhen( true )] out string? buildArtifactRules )
        {
            List<TeamCitySnapshotDependency> dependencies = new();
            consolidatedBuildConfiguration = null;
            nuGetBuildConfiguration = null;
            dependenciesByProjectId = new Dictionary<string, HashSet<string>>();
            nuGetDependencies = new List<TeamCitySnapshotDependency>();
            buildArtifactRules = null;

            foreach ( var buildConfiguration in buildConfigurationsByKind[consolidatedBuildObjectName] )
            {
                var dependencyProjectId = buildConfiguration.ProjectId;

                if ( !product.DependencyDefinition.ProductFamily.TryGetDependencyDefinitionByCiId( dependencyProjectId, out var dependencyDefinition ) )
                {
                    context.Console.WriteError( $"Dependency definition for project '{dependencyProjectId}' not found." );

                    return false;
                }

                if ( dependencyDefinition.ProductFamily == product.ProductFamily
                     && !projectsWithNoNuGetArtifacts.Any( p => dependencyDefinition.Name.Contains( p, StringComparison.Ordinal ) ) )
                {
                    var dependencyPrivateArtifactsDirectory = dependencyDefinition.GetPrivateArtifactsDirectory( configuration )
                        .Replace( Path.DirectorySeparatorChar, '/' );

                    var dependencyPublicArtifactsDirectory = dependencyDefinition.PublicArtifactsDirectory
                        .Replace( Path.DirectorySeparatorChar, '/' );

                    var dependencyName = dependencyDefinition.Name;
                    var artifactRulesFormat = $"+:{{0}}/**/*.{{1}}=>dependencies/{dependencyName}";

                    var packagesArtifactsDirectory = configuration switch
                    {
                        BuildConfiguration.Public => dependencyPublicArtifactsDirectory,
                        _ => dependencyPrivateArtifactsDirectory
                    };

                    string[] rules =
                    [
                        string.Format( CultureInfo.InvariantCulture, artifactRulesFormat, dependencyPrivateArtifactsDirectory, "version.props" ),
                        string.Format( CultureInfo.InvariantCulture, artifactRulesFormat, packagesArtifactsDirectory, "nupkg" ),
                        string.Format( CultureInfo.InvariantCulture, artifactRulesFormat, packagesArtifactsDirectory, "snupkg" )
                    ];

                    var artifactRules = string.Join( "\\n", rules );

                    nuGetDependencies.Add(
                        new TeamCitySnapshotDependency(
                            buildConfiguration.BuildConfigurationId,
                            true,
                            artifactRules ) );
                }

                dependencies.Add(
                    new TeamCitySnapshotDependency(
                        buildConfiguration.BuildConfigurationId,
                        true ) );

                if ( !dependenciesByProjectId.TryGetValue( buildConfiguration.BuildConfigurationId, out var projectDependencies ) )
                {
                    projectDependencies = new HashSet<string>();
                    dependenciesByProjectId.Add( buildConfiguration.ProjectId, projectDependencies );
                }

                // We check for presence, because some dependencies can come from other project families.
                // E.g. PostSharp for Metalama.Vsx.
                projectDependencies.AddRange(
                    buildConfiguration.SnapshotDependencies.Select( d => buildConfigurationsById.TryGetValue( d, out var c ) ? c.ProjectId : null )
                        .Where( c => c != null )
                        .Select( c => c! ) );
            }

            var privateArtifactsDirectory =
                product.GetPrivateArtifactsDirectory( configuration ).Replace( "\\", "/", StringComparison.Ordinal );

            var publicArtifactsDirectory =
                product.PublicArtifactsDirectory.Replace( "\\", "/", StringComparison.Ordinal );

            buildArtifactRules =
                $@"+:{privateArtifactsDirectory}/**/*=>{privateArtifactsDirectory}\n+:{publicArtifactsDirectory}/**/*=>{publicArtifactsDirectory}";

            var nuGetBuildCiId = $"{consolidatedProjectIdPrefix}{nuGetBuildObjectName}";

            dependencies.Add( new TeamCitySnapshotDependency( nuGetBuildCiId, true ) );

            var defaultBuildBranch = configuration switch
            {
                // We should use deploymentBranch here, but TeamCity doesn't support parameterized branches in snapshot dependencies.
                BuildConfiguration.Public => defaultBranch,
                _ => defaultBranch
            };

            consolidatedBuildConfiguration =
                new TeamCityBuildConfiguration(
                    consolidatedBuildObjectName,
                    consolidatedBuildConfigurationName,
                    defaultBuildBranch,
                    defaultBranchParameter,
                    vcsRootId ) { SnapshotDependencies = dependencies.ToArray(), BuildTriggers = consolidatedBuildTriggers };

            DockerSpec? dockerSpec = null;

            if ( product.UseDocker )
            {
                dockerSpec = new DockerSpec( $"{product.ProductNameWithoutDot}-{product.ProductFamily.Version}" );
            }

            var nuGetBuildSteps =
                new TeamCityBuildStep[] { new TeamCityEngineeringBuildBuildStep( configuration, false, dockerSpec, product.BuildTimeoutPlusMargin ) };

            // The default branch is the same as for public build of any other project - see the build configuration of a regular project.
            nuGetBuildConfiguration = new TeamCityBuildConfiguration(
                nuGetBuildObjectName,
                nuGetBuildConfigurationName,
                defaultBranch,
                defaultBranchParameter,
                vcsRootId,
                product.ResolvedBuildAgentRequirements )
            {
                BuildSteps = nuGetBuildSteps, SnapshotDependencies = nuGetDependencies.ToArray(), ArtifactRules = buildArtifactRules
            };

            return true;
        }

        // Debug Build
        const string debugBuildObjectName = "DebugBuild";
        const string debugBuildName = "Build [Debug]";

        if ( !TryPopulateBuildConfigurations(
                BuildConfiguration.Debug,
                debugBuildObjectName,
                debugBuildName,
                [],
                MarkNuGetObjectId( debugBuildObjectName ),
                debugBuildName,
                out var consolidatedDebugBuildConfiguration,
                out var nuGetDebugBuildConfiguration,
                out _,
                out _,
                out _ ) )
        {
            return false;
        }

        tcConfigurations.Add( consolidatedDebugBuildConfiguration );
        nuGetConfigurations.Add( nuGetDebugBuildConfiguration );

        // Downstream Merge

        // Downstream merge of the consolidated build repo itself needs to be done manually,
        // because the TeamCity script needs to be regenerated in each product family version. 

        const string downstreamMergeObjectName = "DownstreamMerge";

        if ( buildConfigurationsByKind.TryGetValue( downstreamMergeObjectName, out var downstreamMergeBuildConfigurations ) )
        {
            var consolidatedDownstreamMergeSnapshotDependencies =
                downstreamMergeBuildConfigurations.Select( c => new TeamCitySnapshotDependency( c.BuildConfigurationId, true ) );

            var consolidatedDownstreamMergeBuildTriggers = new IBuildTrigger[] { new NightlyBuildTrigger( 23, true ) };

            tcConfigurations.Add(
                new TeamCityBuildConfiguration( downstreamMergeObjectName, "Merge Downstream", defaultBranch, defaultBranchParameter, vcsRootId )
                {
                    SnapshotDependencies = consolidatedDownstreamMergeSnapshotDependencies.ToArray(),
                    BuildTriggers = consolidatedDownstreamMergeBuildTriggers
                } );
        }

        // Release Build
        const string releaseBuildObjectName = "ReleaseBuild";
        const string releaseBuildName = "Build [Release]";

        if ( !TryPopulateBuildConfigurations(
                BuildConfiguration.Release,
                releaseBuildObjectName,
                releaseBuildName,
                [],
                MarkNuGetObjectId( releaseBuildObjectName ),
                releaseBuildName,
                out var consolidatedReleaseBuildConfiguration,
                out var nuGetReleaseBuildConfiguration,
                out _,
                out _,
                out _ ) )
        {
            return false;
        }

        tcConfigurations.Add( consolidatedReleaseBuildConfiguration );
        nuGetConfigurations.Add( nuGetReleaseBuildConfiguration );

        // Version bump and public build
        const string publicBuildObjectName = "PublicBuild";
        const string publicBuildName = "Build [Public]";
        var publicConfiguration = BuildConfiguration.Public;

        if ( !TryPopulateBuildConfigurations(
                BuildConfiguration.Public,
                publicBuildObjectName,
                $"3. {publicBuildName}",
                [
                    new NightlyBuildTrigger( 2, true )
                    {
                        // The nightly build is done on the develop branch to find issues early and to prepare for the deployment.
                        // The manually triggered build is done on the release branch to allow for deployment without merge freeze.
                        // Any successful build of the same commit done on the develop branch is reused by TeamCity when deploying from the release branch.
                        BranchFilter = $"+:{defaultBranch}", Parameters = [new TeamCityBuildConfigurationParameter( "DefaultBranch", defaultBranch )]
                    }
                ],
                MarkNuGetObjectId( publicBuildObjectName ),
                publicBuildName,
                out var consolidatedPublicBuildConfiguration,
                out var nuGetPublicBuildConfiguration,
                out var consolidatedPublicBuildSnapshotDependenciesByProjectId,
                out var nuGetPublicBuildDependencies,
                out var nuGetBuildArtifactRules ) )
        {
            return false;
        }

        const string versionBumpObjectName = "VersionBump";

        var consolidatedVersionBumpSteps = new List<TeamCityBuildStep>();
        var consolidatedVersionBumpSourceDependencies = new List<TeamCitySourceDependency>();
        var bumpedProjects = new HashSet<string>();
        var consolidatedVersionBumpParameters = new List<TeamCityBuildConfigurationParameter>();

        var success = true;

        TeamCitySourceDependency CreateSourceDependency( string vcsProjectRootId, string projectName )
            => new( vcsProjectRootId, true, $"+:. => {product.SourceDependenciesDirectory}/{projectName}" );

        TeamCitySourceDependency CreateSourceDependencyFromDefinition( DependencyDefinition dependencyDefinition )
            => CreateSourceDependency( TeamCityHelper.GetVcsRootId( dependencyDefinition ), dependencyDefinition.Name );

        foreach ( var buildConfiguration in buildConfigurationsByKind[publicBuildObjectName] )
        {
            var bumpedProjectId = buildConfiguration.ProjectId;
            var bumpedProjectName = buildConfiguration.ProjectName;

            if ( !product.DependencyDefinition.ProductFamily.TryGetDependencyDefinitionByCiId( bumpedProjectId, out var dependencyDefinition ) )
            {
                context.Console.WriteError( $"Dependency definition for project '{bumpedProjectId}' not found." );

                return false;
            }

            if ( !dependencyDefinition.IsVersioned )
            {
                continue;
            }

            foreach ( var projectDependencyId in consolidatedPublicBuildSnapshotDependenciesByProjectId[bumpedProjectId] )
            {
                if ( !bumpedProjects.Contains( projectDependencyId ) )
                {
                    context.Console.WriteError( $"Incorrect projects order. '{bumpedProjectId}' depends on '{projectDependencyId}', but is ordered earlier." );
                    success = false;
                }
            }

            consolidatedVersionBumpSteps.Add(
                new TeamCityEngineeringCommandBuildStep(
                    $"Bump{bumpedProjectId.Split( '_' ).Last()}",
                    $"Bump version of {bumpedProjectName}",
                    "bump",
                    areCustomArgumentsAllowed: true,
                    dockerSpec: product.DockerSpec ) { WorkingDirectory = $"source-dependencies/{bumpedProjectName}" } );

            consolidatedVersionBumpSourceDependencies.Add( CreateSourceDependencyFromDefinition( dependencyDefinition ) );

            if ( dependencyDefinition.VcsRepository.DefaultBranchParameter != VcsRepository.DefaultDefaultBranchParameter )
            {
                consolidatedVersionBumpParameters.Add(
                    new TeamCityTextBuildConfigurationParameter(
                        dependencyDefinition.VcsRepository.DefaultBranchParameter,
                        dependencyDefinition.VcsRepository.DefaultBranchParameter,
                        $"Default branch of {bumpedProjectName}",
                        dependencyDefinition.Branch ) );
            }

            bumpedProjects.Add( bumpedProjectId );
        }

        if ( !success )
        {
            return false;
        }

        var consolidatedVersionBumpBuildTriggers = new IBuildTrigger[] { new NightlyBuildTrigger( 1, false ) };

        tcConfigurations.Add(
            new TeamCityBuildConfiguration(
                versionBumpObjectName,
                "1. Version Bump",
                defaultBranch,
                defaultBranchParameter,
                vcsRootId,
                product.ResolvedBuildAgentRequirements )
            {
                BuildSteps = consolidatedVersionBumpSteps.ToArray(),
                BuildTriggers = consolidatedVersionBumpBuildTriggers,
                IsDefaultVcsRootUsed = false,
                SourceDependencies = consolidatedVersionBumpSourceDependencies.ToArray(),
                IsSshAgentRequired = true,
                Parameters = consolidatedVersionBumpParameters.ToArray()
            } );

        bool TryAddPreOrPostDeploymentBuildConfiguration(
            string objectName,
            string name,
            string command,
            string commandName,
            Func<DependencyDefinition, string?> getBranch )
        {
            List<TeamCitySourceDependency> sourceDependencies = new();
            List<TeamCitySnapshotDependency> snapshotDependencies = new();
            List<TeamCityBuildStep> steps = new();
            List<TeamCityBuildConfigurationParameter> parameters = new();

            if ( product.MainVersionDependency == null )
            {
                context.Console.WriteError( "Main version dependency is not set for the consolidated project." );

                return false;
            }

            foreach ( var project in subprojects )
            {
                if ( project.Id == consolidatedProjectId.Id || project.Id == $"{consolidatedProjectId.Id}_NuGet" )
                {
                    continue;
                }

                if ( !product.ProductFamily.TryGetDependencyDefinitionByCiId( project.Id, out var projectDependencyDefinition ) )
                {
                    // This is a container for other projects.
                    continue;
                }

                sourceDependencies.Add( CreateSourceDependencyFromDefinition( projectDependencyDefinition ) );

                if ( projectDependencyDefinition.VcsRepository.DefaultBranchParameter != VcsRepository.DefaultDefaultBranchParameter )
                {
                    var dependencyBranch = getBranch( projectDependencyDefinition );

                    if ( dependencyBranch == null )
                    {
                        context.Console.WriteError( $"The '{projectDependencyDefinition.Name}' doesn't have the required branch set for {command}ing." );

                        return false;
                    }

                    parameters.Add(
                        new TeamCityTextBuildConfigurationParameter(
                            projectDependencyDefinition.VcsRepository.DefaultBranchParameter,
                            projectDependencyDefinition.VcsRepository.DefaultBranchParameter,
                            $"Default branch of {project.Name}",
                            dependencyBranch ) );
                }

                var projectRelativeId = project.Id.Split( '_' ).Last();

                steps.Add(
                    new TeamCityEngineeringCommandBuildStep(
                        $"{objectName}_{projectRelativeId}",
                        $"{commandName} deployment of {project.Name}",
                        command,
                        "--configuration Public --buildNumber %build.number% --buildType %system.teamcity.buildType.id% --use-local-dependencies",
                        areCustomArgumentsAllowed: true,
                        dockerSpec: product.DockerSpec ) { WorkingDirectory = $"source-dependencies/{project.Name}" } );

                // Dependencies outside of the product family are fetched from the artifacts.
                if ( buildConfigurationsById.TryGetValue( $"{project.Id}_{publicBuildObjectName}", out var publicBuildConfiguration ) )
                {
                    // If not found, the project is not published.

                    foreach ( var dependencyConfigurationId in publicBuildConfiguration.SnapshotDependencies )
                    {
                        var dependencyProjectId = string.Join( '_', dependencyConfigurationId.Split( '_' ).SkipLast( 1 ) );

                        if ( !product.ProductFamily.TryGetDependencyDefinitionByCiId( dependencyProjectId, out var dependencyDefinition ) )
                        {
                            context.Console.WriteError(
                                $"Dependency definition for project '{dependencyProjectId}' (configuration '{dependencyConfigurationId}') not found." );

                            return false;
                        }

                        if ( dependencyDefinition.ProductFamily != projectDependencyDefinition.ProductFamily )
                        {
                            var dependencyName = dependencyDefinition.Name;

                            var dependencyPrivateArtifactsDirectory = dependencyDefinition.GetPrivateArtifactsDirectory( BuildConfiguration.Public )
                                .Replace( Path.DirectorySeparatorChar, '/' );

                            var artifactRules =
                                $"+:{dependencyPrivateArtifactsDirectory}/{dependencyName}.version.props=>source-dependencies/{project.Name}/dependencies/{dependencyName}";

                            snapshotDependencies.Add( new TeamCitySnapshotDependency( dependencyConfigurationId, true, artifactRules ) );
                        }
                    }
                }
            }

            sourceDependencies.Add( CreateSourceDependencyFromDefinition( product.DependencyDefinition ) );

            steps.Add(
                new TeamCityEngineeringCommandBuildStep(
                    $"{objectName}_{consolidatedProjectId.Id}",
                    $"{commandName} consolidated deployment",
                    command,
                    "--configuration Public --buildNumber %build.number% --buildType %system.teamcity.buildType.id% --use-local-dependencies",
                    dockerSpec: product.DockerSpec ) { WorkingDirectory = $"source-dependencies/{consolidatedProjectName}" } );

            var branch = getBranch( product.DependencyDefinition );

            if ( branch == null )
            {
                context.Console.WriteError( $"The consolidated project doesn't have the required branch set for {command}ing." );

                return false;
            }

            tcConfigurations.Add(
                new TeamCityBuildConfiguration(
                    objectName,
                    name,
                    branch,
                    defaultBranchParameter,
                    vcsRootId,
                    product.ResolvedBuildAgentRequirements )
                {
                    BuildSteps = steps.ToArray(),
                    SourceDependencies = sourceDependencies.ToArray(),
                    SnapshotDependencies = snapshotDependencies.ToArray(),
                    IsDefaultVcsRootUsed = false,
                    IsSshAgentRequired = true,
                    Parameters = parameters.ToArray()
                } );

            return true;
        }

        // Pre-deployment
        const string preDeploymentObjectName = "PreDeployment";
        const string preDeploymentName = "2. Prepare Deployment [Public]";

        if ( !TryAddPreOrPostDeploymentBuildConfiguration( preDeploymentObjectName, preDeploymentName, "prepublish", "Prepare", d => d.Branch ) )
        {
            return false;
        }

        tcConfigurations.Add( consolidatedPublicBuildConfiguration );
        nuGetConfigurations.Add( nuGetPublicBuildConfiguration );

        // Public deployment
        const string publicDeploymentObjectName = "PublicDeployment";
        const string publicDeploymentName = "Deploy [Public]";
        var publicConsolidatedBuildCiId = $"{consolidatedProjectIdPrefix}{publicBuildObjectName}";
        var publicNuGetBuildCiId = $"{consolidatedProjectIdPrefix}{MarkNuGetObjectId( publicBuildObjectName )}";
        var publicNuGetDeploymentCiId = $"{consolidatedProjectIdPrefix}{MarkNuGetObjectId( publicDeploymentObjectName )}";

        var nuGetPublicDeploymentSteps = new TeamCityBuildStep[]
        {
            new TeamCityEngineeringPublishBuildStep( publicConfiguration, product.DockerSpec, product.BuildTimeoutPlusMargin )
        };

        // TODO: Only Public builds of dependencies that define version need to be included.
        //       Here we include all Public builds which will cause download of all artifacts.
        var nuGetPublicDeploymentDependencies =
            nuGetPublicBuildDependencies
                .Select( d => new TeamCitySnapshotDependency(
                             d.ObjectId.Replace( $"_{publicBuildObjectName}", $"_{publicDeploymentObjectName}", StringComparison.Ordinal ),
                             true ) )
                .Concat( nuGetPublicBuildDependencies )
                .Append( new TeamCitySnapshotDependency( publicNuGetBuildCiId, true, nuGetBuildArtifactRules ) );

        nuGetConfigurations.Add(
            new TeamCityBuildConfiguration(
                MarkNuGetObjectId( publicDeploymentObjectName ),
                publicDeploymentName,
                deploymentBranch,
                defaultBranchParameter,
                vcsRootId,
                product.ResolvedBuildAgentRequirements )
            {
                BuildSteps = nuGetPublicDeploymentSteps,
                SnapshotDependencies = nuGetPublicDeploymentDependencies.ToArray(),
                BuildTimeOutThreshold = product.DeploymentTimeout,
                IsDeployment = true
            } );

        var publicDeploymentBuildConfigurations = buildConfigurationsByKind[publicDeploymentObjectName];
        var publicDeploymentBuildConfigurationIds = publicDeploymentBuildConfigurations.Select( c => c.BuildConfigurationId ).ToArray();

        // Include dependants of the public deployment build configurations, like search update.
        var publicDeploymentDependants = buildConfigurations.Where( c => c.SnapshotDependencies.Intersect( publicDeploymentBuildConfigurationIds ).Any() )
            .Select( c => c.BuildConfigurationId )
            .Where( c => !c.StartsWith( consolidatedProjectIdPrefix, StringComparison.Ordinal ) )
            .Except( publicDeploymentBuildConfigurationIds )
            .ToArray();

        var consolidatedPublicDeploymentSnapshotDependencies =
            publicDeploymentBuildConfigurationIds
                .Concat( publicDeploymentDependants )
                .Select( c => new TeamCitySnapshotDependency( c, true ) )
                .Append( new TeamCitySnapshotDependency( publicConsolidatedBuildCiId, true ) )
                .Append( new TeamCitySnapshotDependency( publicNuGetDeploymentCiId, true ) );

        tcConfigurations.Add(
            new TeamCityBuildConfiguration(
                publicDeploymentObjectName,
                $"4. {publicDeploymentName}",

                // We should use deploymentBranch here, but TeamCity doesn't support parameterized branches in snapshot dependencies.
                defaultBranch,
                defaultBranchParameter,
                vcsRootId,
                product.ResolvedBuildAgentRequirements )
            {
                SnapshotDependencies = consolidatedPublicDeploymentSnapshotDependencies.ToArray(), IsDeployment = true
            } );

        // Post-deployment
        const string postDeploymentObjectName = "PostDeployment";
        const string postDeploymentName = "5. Finish Deployment [Public]";

        if ( !TryAddPreOrPostDeploymentBuildConfiguration( postDeploymentObjectName, postDeploymentName, "postpublish", "Finish", d => d.ReleaseBranch ) )
        {
            return false;
        }

        // Add NuGet ZIP project
        var nuGetProject = new TeamCityProject( "NuGet", "NuGet", nuGetConfigurations.ToArray() );

        var tcProject = new TeamCityProject( tcConfigurations.ToArray(), [nuGetProject] );

        GeneratePom( context, consolidatedProjectId.Id, product.DependencyDefinition.CiConfiguration.BaseUrl );
        GenerateTeamCityConfiguration( context, tcProject );

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