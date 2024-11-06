// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.Search;

[PublicAPI]
public class UpdateSearchProductExtension<TUpdateSearchCommand> : ProductExtension where TUpdateSearchCommand : UpdateSearchCommandBase
{
    public string TypesenseUri { get; }

    public string Source { get; }

    public string SourceUrl { get; }

    public bool IgnoreTls { get; }

    public BuildConfiguration[] BuildConfigurations { get; }

    public TimeSpan TimeOutThreshold { get; }

    public string? CustomBuildConfigurationName { get; }

    public ConfigurationSpecific<IBuildTrigger[]?>? BuildTriggers { get; }

    public UpdateSearchProductExtension(
        string typesenseUri,
        string source,
        string sourceUrl,
        bool ignoreTls = false,
        BuildConfiguration[]? buildConfigurations = null,
        TimeSpan? timeOutThreshold = null,
        string? customBuildConfigurationName = null,
        ConfigurationSpecific<IBuildTrigger[]?>? buildTriggers = null )
    {
        this.TypesenseUri = typesenseUri;
        this.Source = source;
        this.SourceUrl = sourceUrl;
        this.IgnoreTls = ignoreTls;
        this.BuildConfigurations = buildConfigurations ?? [BuildConfiguration.Public];
        this.TimeOutThreshold = timeOutThreshold ?? TimeSpan.FromMinutes( 5 );
        this.CustomBuildConfigurationName = customBuildConfigurationName;
        this.BuildTriggers = buildTriggers;
    }

    internal override bool AddTeamcityBuildConfiguration( BuildContext context, List<TeamCityBuildConfiguration> teamCityBuildConfigurations )
    {
        TeamCityBuildStep CreateBuildStep()
        {
            var arguments = new List<string> { this.TypesenseUri, this.Source, this.SourceUrl };

            if ( this.IgnoreTls )
            {
                arguments.Add( "--ignore-tls" );
            }

            return new TeamCityEngineeringCommandBuildStep( "UpdateSearch", "Update search", "tools search update", string.Join( " ", arguments ), true );
        }

        foreach ( var configuration in this.BuildConfigurations )
        {
            var configurationInfo = context.Product.Configurations[configuration];

            var name = this.CustomBuildConfigurationName ?? $"Update Search [{configuration}]";

            var dependencies = configurationInfo.ExportsToTeamCityDeploy
                ? new[] { new TeamCitySnapshotDependency( $"{configuration}Deployment", false ) }
                : null;

            var buildTriggers = this.BuildTriggers?[configuration];
            var vcsRootId = TeamCityHelper.GetVcsRootId( context.Product.DependencyDefinition );
            var buildAgentRequirements = context.Product.ResolvedBuildAgentRequirements;

            var teamCityUpdateSearchConfiguration = new TeamCityBuildConfiguration(
                $"{configuration}UpdateSearch",
                name,
                context.Product.DependencyDefinition.ReleaseBranch ?? context.Product.DependencyDefinition.Branch,
                context.Product.DependencyDefinition.VcsRepository.DefaultBranchParameter,
                vcsRootId,
                buildAgentRequirements )
            {
                BuildSteps = [CreateBuildStep()],
                IsDeployment = true,
                SnapshotDependencies = dependencies,
                BuildTimeOutThreshold = this.TimeOutThreshold,
                BuildTriggers = buildTriggers
            };

            teamCityBuildConfigurations.Add( teamCityUpdateSearchConfiguration );

            if ( configurationInfo.ExportsToTeamCityDeployWithoutDependencies )
            {
                var teamCityUpdateSearchWithoutDependenciesConfiguration = new TeamCityBuildConfiguration(
                    $"{configuration}UpdateSearchNoDependency",
                    $"Standalone {name}",
                    context.Product.DependencyDefinition.Branch,
                    context.Product.DependencyDefinition.VcsRepository.DefaultBranchParameter,
                    vcsRootId,
                    buildAgentRequirements ) { BuildSteps = [CreateBuildStep()], IsDeployment = true, BuildTimeOutThreshold = this.TimeOutThreshold };

                teamCityBuildConfigurations.Add( teamCityUpdateSearchWithoutDependenciesConfiguration );
            }
        }

        return true;
    }

    internal override bool AddTool( IConfigurator<CommandSettings> tools )
    {
        tools.AddBranch(
            "search",
            search =>
            {
                search.AddCommand<TUpdateSearchCommand>( "update" )
                    .WithDescription( "Updates a search collection from the given source or writes data to the console when --dry option is used." )
                    .WithExample( "tools", "search", "update", "http://localhost:8108", "metalamadoc", "https://doc.example.com/sitemap.xml" )
                    .WithExample(
                        "tools",
                        "search",
                        "update",
                        "http://localhost:8108",
                        "metalamadoc",
                        "https://doc.example.com/conceptual/tryme",
                        "--single",
                        "--dry" );
            } );

        return true;
    }
}