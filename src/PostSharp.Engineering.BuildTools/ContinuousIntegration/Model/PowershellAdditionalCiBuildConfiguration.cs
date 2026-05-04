// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using PostSharp.Engineering.BuildTools.Docker;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

    public bool UseWsl { get; init; }

    internal override TeamCityBuildConfiguration TeamCityBuildConfiguration(
        ProductProperties productProperties,
        IReadOnlyDictionary<BuildConfiguration, TeamCityBuildConfiguration> teamCityBuildBuildConfigurations )
    {
        var product = productProperties.Product;

        var buildSteps = new List<BuildStep>();
        List<TeamCitySnapshotDependency>? snapshotDependencies = null;

        // Handle snapshot dependencies.
        if ( this.BuildSnapshotDependency != null )
        {
            if ( !teamCityBuildBuildConfigurations.TryGetValue( this.BuildSnapshotDependency.Value, out var buildConfiguration ) )
            {
                throw new KeyNotFoundException( $"Cannot find the TeamCity build configuration for '{this.BuildSnapshotDependency.Value}'." );
            }

            var buildArtifactsDirectory = productProperties.Product.GetPrivateArtifactsRelativeDirectory( this.BuildSnapshotDependency.Value )
                .Replace( "\\", "/", StringComparison.Ordinal );

            // Get all transitive dependencies for the build configuration
            var dependencies = product.DependencyDefinition.GetAllDependencies( this.BuildSnapshotDependency.Value )
                .Where( d => d.Definition.GenerateSnapshotDependency )
                .ToList();

            // Create snapshot dependencies for all transitive dependencies
            var reuseBuilds = this.ReuseLastSuccessfulBuild ? ReuseBuilds.LastSuccessful : ReuseBuilds.Default;

            snapshotDependencies =
                [new TeamCitySnapshotDependency( buildConfiguration.ObjectName, false, $"+:{buildArtifactsDirectory}/**/*=>{buildArtifactsDirectory}", ReuseBuilds: reuseBuilds )];

            snapshotDependencies.AddRange(
                dependencies.Select( d => new TeamCitySnapshotDependency(
                                         d.Definition.CiConfiguration.BuildTypes[d.Configuration],
                                         true,
                                         $"+:{d.Definition.GetPrivateArtifactsDirectory( d.Configuration ).Replace( Path.DirectorySeparatorChar, '/' )}/**/*=>dependencies/{d.Key}",
                                         ReuseBuilds: reuseBuilds ) ) );

            // If we have a build snapshot dependency, copy nuget.restored.config to nuget.config
            var copyNuGetConfigCommand = $@"Copy-Item -Path ""{buildArtifactsDirectory}/nuget.restored.config"" -Destination ""nuget.config"" -Force;";

            if ( product.AddWslSupport )
            {
                copyNuGetConfigCommand += $@"Copy-Item -Path ""{buildArtifactsDirectory}/nuget.restored.config"" -Destination ""nuget.wsl.config"" -Force;";
            }

            buildSteps.Add(
                new PowerShellCommandBuildStep(
                    "CopyNuGetConfig",
                    "Copy nuget.restored.config to nuget.config",
                    copyNuGetConfigCommand,
                    null ) );

            // Create an MSBuild project that imports the restored version props file and all dependency version props
            // Paths are relative to eng/Versions.g.props, so need ../ prefix
            var versionImports = $"<Import Project=`\"../{buildArtifactsDirectory}/{product.ProductName}.version.props`\" />";

            foreach ( var dependency in dependencies )
            {
                versionImports +=
                    $"<Import Project=`\"../dependencies/{dependency.Key}/{dependency.Key}.version.props`\" />";
            }

            var createVersionsFileCommand =
                $@"New-Item -Path ""{product.EngineeringDirectory}/Versions.g.props"" -ItemType File -Force -Value ""<Project>{versionImports}</Project>"" | Out-Null;";

            buildSteps.Add(
                new PowerShellCommandBuildStep(
                    "CreateVersionsFile",
                    "Create eng/Versions.g.props",
                    createVersionsFileCommand,
                    null ) );
        }

        // Add the main execution step
        buildSteps.Add(
            new PowerShellScriptBuildStep(
                "Exec",
                $"Execute {this.Script}",
                this.Script,
                this.Arguments,
                this.BuildAgentRequirements == null
                    ? (this.Dockerfile != null && product.DockerSpec != null ? product.DockerSpec with { Dockerfile = this.Dockerfile } : product.DockerSpec)
                    : this.BuildAgentRequirements is ContainerHostRequirements containerHostRequirements
                        ? new DockerSpec(
                            $"{productProperties.Product.ProductNameWithoutDot}-{productProperties.Product.ProductFamily.Version}-{this.Id}".ToLowerInvariant(),
                            Dockerfile: this.Dockerfile )
                        : null,
                true )
            {
#pragma warning disable CS0612 // Type or member is obsolete
                UseWsl = this.UseWsl || this.BuildAgentRequirements is ContainerHostRequirements { HostKind: ContainerHostKind.Wsl }
#pragma warning restore CS0612 // Type or member is obsolete
            } );

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
            SnapshotDependencies = snapshotDependencies?.ToArray(),
            Parameters = this.Parameters
        };

        return downstreamMergeConfiguration;
    }
}