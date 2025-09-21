// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Triggers;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Search.Backends;
using PostSharp.Engineering.BuildTools.Search.Crawlers;
using PostSharp.Engineering.BuildTools.Search.Updaters;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace PostSharp.Engineering.BuildTools.Search;

[PublicAPI]
public class UpdateSearchProductExtension : ProductExtension
{
    private readonly Func<SearchBackendBase, CollectionUpdater> _createUpdater;

    public string TypesenseUri { get; }

    public string Source { get; }

    public string SourceUrl { get; }

    public BuildConfiguration[] BuildConfigurations { get; }

    public TimeSpan TimeOutThreshold { get; }

    public string? CustomBuildConfigurationName { get; }

    public ConfigurationSpecific<IBuildTrigger[]?>? BuildTriggers { get; }

    public UpdateSearchProductExtension(
        string typesenseUri,
        string source,
        string sourceUrl,
        Func<DocumentParser> createParser,
        ImmutableArray<string> products,
        BuildConfiguration[]? buildConfigurations = null,
        TimeSpan? timeOutThreshold = null,
        string? customBuildConfigurationName = null,
        ConfigurationSpecific<IBuildTrigger[]?>? buildTriggers = null ) : this(
        typesenseUri,
        source,
        sourceUrl,
        searchBackend => new DocumentationUpdater( products, new DocumentParserFactory( createParser ), searchBackend ),
        buildConfigurations,
        timeOutThreshold,
        customBuildConfigurationName,
        buildTriggers ) { }

    public UpdateSearchProductExtension(
        string typesenseUri,
        string source,
        string sourceUrl,
        Func<SearchBackendBase, CollectionUpdater> createUpdater,
        BuildConfiguration[]? buildConfigurations = null,
        TimeSpan? timeOutThreshold = null,
        string? customBuildConfigurationName = null,
        ConfigurationSpecific<IBuildTrigger[]?>? buildTriggers = null )
    {
        this._createUpdater = createUpdater;
        this.TypesenseUri = typesenseUri;
        this.Source = source;
        this.SourceUrl = sourceUrl;
        this.BuildConfigurations = buildConfigurations ?? [BuildConfiguration.Public];
        this.TimeOutThreshold = timeOutThreshold ?? TimeSpan.FromMinutes( 30 );
        this.CustomBuildConfigurationName = customBuildConfigurationName;
        this.BuildTriggers = buildTriggers;
    }
    
    
    internal CollectionUpdater CreateUpdater( SearchBackendBase searchBackend )
    {
        return this._createUpdater( searchBackend );
    }

    internal override bool AddTeamcityBuildConfiguration( BuildContext context, List<TeamCityBuildConfiguration> teamCityBuildConfigurations )
    {
        TeamCityBuildStep CreateBuildStep()
        {
            return new TeamCityEngineeringCommandBuildStep( "UpdateSearch", "Update search", "search update", null, true );
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
                context.Product.DependencyDefinition.PublishingBranch,
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

    internal override bool AddCommands( IConfigurator root, BaseCommandData data )
    {
        root.AddBranch(
            "search",
            search =>
            {
                search.AddCommand<UpdateSearchCommand>( "update" )
                    .WithData( data )
                    .WithDescription( "Updates a search collection from the given source or writes data to the console when --dry option is used." );
            } );

        return true;
    }
}