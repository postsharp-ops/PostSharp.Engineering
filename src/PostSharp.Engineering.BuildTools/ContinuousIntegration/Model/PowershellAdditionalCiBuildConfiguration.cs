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

        var buildSteps = new List<BuildStep>();
        TeamCityBuildConfiguration? buildConfiguration = null;
        string? buildArtifactsDirectory = null; 

        // Handle snapshot dependencies.
        if ( this.BuildSnapshotDependency != null )
        {
            if ( !teamCityBuildBuildConfigurations.TryGetValue( this.BuildSnapshotDependency.Value, out buildConfiguration ) )
            {
                throw new KeyNotFoundException( $"Cannot find the TeamCity build configuration for '{this.BuildSnapshotDependency.Value}'." );
            }

            buildArtifactsDirectory = productProperties.Product.GetPrivateArtifactsRelativeDirectory( this.BuildSnapshotDependency.Value ).Replace( "\\", "/", StringComparison.Ordinal );
            
            // If we have a build snapshot dependency, copy nuget.restored.config to nuget.config
            buildSteps.Add(
                new PowerShellCommandBuildStep(
                    "CopyNuGetConfig",
                    "Copy nuget.restored.config to nuget.config",
                    $@"Copy-Item -Path ""{buildArtifactsDirectory}/nuget.restored.config"" -Destination ""nuget.config"" -Force",
                    null ) );
        }
        
        // Add the main execution step
        buildSteps.Add(
            new PowerShellScriptBuildStep(
                "Exec",
                $"Execute {this.Script}",
                this.Script,
                this.Arguments,
                this.BuildAgentRequirements == null ? product.DockerSpec : this.BuildAgentRequirements.IsDockerized ? new DockerSpec( $"{productProperties.Product.ProductNameWithoutDot}-{productProperties.Product.ProductFamily.Version}-{this.Id}".ToLowerInvariant() ) : null,
                true ) );

        // Build the configuration.
        var downstreamMergeConfiguration = new TeamCityBuildConfiguration(
            this.Id,
            this.Name,
            this.Branch ?? productProperties.Branch,
            productProperties.VcsId,
            this.BuildAgentRequirements ?? product.ResolvedBuildAgentRequirements )
        {
            BuildSteps = buildSteps.ToArray(),
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