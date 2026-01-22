// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using PostSharp.Engineering.BuildTools.Docker;
using System;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

[PublicAPI]
public class PowershellAdditionalCiBuildConfiguration : AdditionalCiBuildConfiguration
{
    public PowershellAdditionalCiBuildConfiguration( string id, string name, string script, string arguments ) : base( id, name )
    {
        this.Script = script;
        this.Arguments = arguments;
    }

    public string Script { get; }

    public string Arguments { get; }

    internal override TeamCityBuildConfiguration TeamCityBuildConfiguration(
        ProductProperties productProperties,
        IReadOnlyDictionary<BuildConfiguration, TeamCityBuildConfiguration> teamCityBuildBuildConfigurations )
    {
        var product = productProperties.Product;

        TeamCityBuildConfiguration? buildConfiguration = null;
        string? buildArtifactsDirectory = null; 

        if ( this.BuildSnapshotDependency != null )
        {
            if ( !teamCityBuildBuildConfigurations.TryGetValue( this.BuildSnapshotDependency.Value, out buildConfiguration ) )
            {
                throw new KeyNotFoundException( $"Cannot find the TeamCity build configuration for '{this.BuildSnapshotDependency.Value}'." );
            }

            buildArtifactsDirectory = productProperties.Product.GetPrivateArtifactsRelativeDirectory( this.BuildSnapshotDependency.Value ).Replace( "\\", "/", StringComparison.Ordinal );
        }

        var downstreamMergeConfiguration = new TeamCityBuildConfiguration(
            this.Id,
            this.Name,
            this.Branch ?? productProperties.Branch,
            productProperties.VcsId,
            this.BuildAgentRequirements ?? product.ResolvedBuildAgentRequirements )
        {
            BuildSteps =
            [
                new PowerShellBuildStep(
                    "Exec",
                    $"Execute {this.Script}",
                    this.Script,
                    this.Arguments,
                    this.BuildAgentRequirements == null ? product.DockerSpec : this.BuildAgentRequirements.IsDockerized ? new DockerSpec( $"{productProperties.Product.ProductNameWithoutDot}-{productProperties.Product.ProductFamily.Version}-{this.Id}".ToLowerInvariant() ) : null,
                    true )
            ],
            IsSshAgentRequired = productProperties.IsRepoRemoteSsh,
            SourceDependencies = this.SourceDependenciesRequirements switch
            {
                SourceDependenciesRequirements.None => [],
#pragma warning disable CS0618 // Type or member is obsolete
                SourceDependenciesRequirements.EngOnly => productProperties.EngOnlySourceDependencies,
#pragma warning restore CS0618 // Type or member is obsolete
                SourceDependenciesRequirements.Full => productProperties.SourceDependencies,
                _ => throw new ArgumentOutOfRangeException()
            },
            SnapshotDependencies = buildConfiguration == null
                ? null
                : [new TeamCitySnapshotDependency( buildConfiguration.ObjectName, false, $"+:{buildArtifactsDirectory}/**/*=>{buildArtifactsDirectory}" )]
        };

        return downstreamMergeConfiguration;
    }
}