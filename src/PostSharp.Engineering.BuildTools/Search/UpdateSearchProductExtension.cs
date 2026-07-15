// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Triggers;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Search.Backends;
using PostSharp.Engineering.BuildTools.Search.Crawlers;
using PostSharp.Engineering.BuildTools.Search.Updaters;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Search;

[PublicAPI]
public class UpdateSearchProductExtension : ProductExtension
{
    private readonly Func<SearchBackendBase, CollectionUpdater> _createUpdater;

    public string TypesenseUri { get; }

    public string Source { get; }

    public string SourceUrl { get; }

    public BuildConfiguration[] BuildConfigurations { get; }

    public TimeSpan TimeOut { get; }

    public string? CustomBuildConfigurationName { get; }

    public ConfigurationSpecific<IBuildTrigger[]?>? BuildTriggers { get; }

    /// <summary>
    /// Gets a value indicating whether the generated TeamCity deployment runs an incremental update
    /// (<c>search update &lt;source&gt; --incremental</c>) instead of a full rebuild.
    /// </summary>
    public bool Incremental { get; }

    public UpdateSearchProductExtension(
        string typesenseUri,
        string source,
        string sourceUrl,
        Func<DocumentParser> createParser,
        ImmutableArray<string> products,
        BuildConfiguration[]? buildConfigurations = null,
        TimeSpan? timeOutThreshold = null,
        string? customBuildConfigurationName = null,
        ConfigurationSpecific<IBuildTrigger[]?>? buildTriggers = null,
        bool incremental = false ) : this(
        typesenseUri,
        source,
        sourceUrl,
        searchBackend => new DocumentationUpdater( source, sourceUrl, products, new DocumentParserFactory( createParser ), searchBackend ),
        buildConfigurations,
        timeOutThreshold,
        customBuildConfigurationName,
        buildTriggers,
        incremental ) { }

    public UpdateSearchProductExtension(
        string typesenseUri,
        string source,
        string sourceUrl,
        Func<SearchBackendBase, CollectionUpdater> createUpdater,
        BuildConfiguration[]? buildConfigurations = null,
        TimeSpan? timeOutThreshold = null,
        string? customBuildConfigurationName = null,
        ConfigurationSpecific<IBuildTrigger[]?>? buildTriggers = null,
        bool incremental = false )
    {
        this._createUpdater = createUpdater;
        this.TypesenseUri = typesenseUri;
        this.Source = source;
        this.SourceUrl = sourceUrl;
        this.BuildConfigurations = buildConfigurations ?? [BuildConfiguration.Public];
        this.TimeOut = timeOutThreshold ?? TimeSpan.FromMinutes( 30 );
        this.CustomBuildConfigurationName = customBuildConfigurationName;
        this.BuildTriggers = buildTriggers;
        this.Incremental = incremental;
    }

    internal CollectionUpdater CreateUpdater( SearchBackendBase searchBackend )
    {
        return this._createUpdater( searchBackend );
    }

    internal override bool AddTeamcityBuildConfiguration( BuildContext context, List<TeamCityBuildConfiguration> teamCityBuildConfigurations )
    {
        // When the product declares several search collections, each gets its own deployment build
        // configuration. The [source] argument selects the collection and is appended both to the
        // TeamCity object ids (to keep them unique) and to the 'search update' command line.
        var hasMultipleCollections = context.Product.Extensions.OfType<UpdateSearchProductExtension>().Count() > 1;

        // TeamCity object ids only allow [A-Za-z0-9_], so sanitize the source (e.g. "postsharp-web").
        var idSuffix = hasMultipleCollections
            ? "_" + new string( this.Source.Where( char.IsLetterOrDigit ).ToArray() )
            : "";

        var command = hasMultipleCollections ? $"search update {this.Source}" : "search update";

        if ( this.Incremental )
        {
            command += " --incremental";
        }

        BuildStep CreateBuildStep()
        {
            return new EngineeringCommandBuildStep( "UpdateSearch", "Update search", command, null, true, timeout: this.TimeOut );
        }

        foreach ( var configuration in this.BuildConfigurations )
        {
            var configurationInfo = context.Product.Configurations[configuration];

            var name = this.CustomBuildConfigurationName
                       ?? ( hasMultipleCollections
                           ? $"Update Search {this.Source} [{configuration}]"
                           : $"Update Search [{configuration}]" );

            var dependencies = configurationInfo.ExportsToTeamCityDeploy
                ? new[] { new TeamCitySnapshotDependency( $"{configuration}Deployment", false ) }
                : null;

            var buildTriggers = this.BuildTriggers?[configuration];
            var vcsRootId = TeamCityHelper.GetVcsId( context.Product.DependencyDefinition );
            var buildAgentRequirements = context.Product.ResolvedBuildAgentRequirements;

            var teamCityUpdateSearchConfiguration = new TeamCityBuildConfiguration(
                $"{configuration}UpdateSearch{idSuffix}",
                name,
                context.Product.DependencyDefinition.PublishingBranch,
                vcsRootId,
                buildAgentRequirements )
            {
                BuildSteps = [CreateBuildStep()], IsDeployment = true, SnapshotDependencies = dependencies, BuildTriggers = buildTriggers
            };

            teamCityBuildConfigurations.Add( teamCityUpdateSearchConfiguration );

            if ( configurationInfo.ExportsToTeamCityDeployWithoutDependencies )
            {
                var teamCityUpdateSearchWithoutDependenciesConfiguration = new TeamCityBuildConfiguration(
                    $"{configuration}UpdateSearchNoDependency{idSuffix}",
                    $"Standalone {name}",
                    context.Product.DependencyDefinition.Branch,
                    vcsRootId,
                    buildAgentRequirements ) { BuildSteps = [CreateBuildStep()], IsDeployment = true };

                teamCityBuildConfigurations.Add( teamCityUpdateSearchWithoutDependenciesConfiguration );
            }
        }

        return true;
    }

    internal override bool AddCommands( IConfigurator root, BaseCommandData data )
    {
        // A product may declare several search collections (several UpdateSearchProductExtension instances).
        // They all share a single 'search update [source]' command, so only the first extension registers it.
        // The command selects which collection to update from the optional [source] argument.
        var searchExtensions = data.Product.Extensions.OfType<UpdateSearchProductExtension>().ToList();

        if ( searchExtensions.Count > 0 && !ReferenceEquals( searchExtensions[0], this ) )
        {
            return true;
        }

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