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

    /// <summary>
    /// Gets the path of the script as the agent sees it, relative to the repository root. It is <see cref="Script"/>
    /// itself unless a configuration generates its script somewhere other than the root, in which case the location
    /// is only known once the product is known.
    /// </summary>
    internal virtual string GetScriptPath( ProductProperties productProperties ) => this.Script;

    public string Arguments { get; }

    public bool UseWsl { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether this configuration starts containers without running inside
    /// one. Such a configuration has no image-preparation step, so it would otherwise miss the cleanup step
    /// that runs the agent's BUILDAGENT_CLEANUP_SCRIPT. A Docker test configuration sets it.
    /// </summary>
    public bool StartsContainers { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether this configuration consumes the artifacts of the products this
    /// product depends on, as the build configurations of the product itself do.
    /// </summary>
    /// <remarks>
    /// A configuration that depends on a stage of its own product consumes them in any case, so this is for a
    /// configuration that depends on no such stage: the first stage of a pipeline, which compiles the product and
    /// therefore needs what the product is compiled against. It cannot be the default, because an additional
    /// configuration that neither builds nor tests the product, such as a version bump or a downstream merge, would
    /// then wait for a full build of every dependency and use none of it.
    /// </remarks>
    public bool ConsumesProductDependencies { get; init; }

    internal override TeamCityBuildConfiguration TeamCityBuildConfiguration(
        ProductProperties productProperties,
        IReadOnlyDictionary<BuildConfiguration, TeamCityBuildConfiguration> teamCityBuildBuildConfigurations )
    {
        var product = productProperties.Product;

        var buildSteps = new List<BuildStep>();
        List<TeamCitySnapshotDependency>? snapshotDependencies = null;

        // Handle snapshot dependencies. A dependency is either one of the product build configurations or, for a
        // product whose pipeline has intermediate build configurations, another additional configuration named by
        // identifier. The artifact layout is the same either way, so everything below is unaffected by which it is.
        var declaredSnapshotDependencies = this.GetSnapshotDependencies();

        // The products this product is built against, which is a separate question from the dependencies within the
        // product. A configuration that consumes a stage of its own product takes them, because it continues a
        // checkout that was built against them. A configuration that consumes no such stage takes them only when it
        // sets ConsumesProductDependencies, which is what the first stage of a pipeline does: it compiles the product
        // and therefore needs what the product is compiled against.
        var productDependencies = declaredSnapshotDependencies.Length > 0 || this.ConsumesProductDependencies
            ? product.DependencyDefinition.GetAllDependencies( this.EffectiveArtifactsConfiguration )
                .Where( d => d.Definition.GenerateSnapshotDependency )
                .ToList()
            : [];

        if ( declaredSnapshotDependencies.Length > 0 || productDependencies.Count > 0 )
        {
            var artifactsConfiguration = this.EffectiveArtifactsConfiguration;

            var buildArtifactsDirectory = product.GetPrivateArtifactsRelativeDirectory( artifactsConfiguration )
                .Replace( "\\", "/", StringComparison.Ordinal );

            // Create snapshot dependencies for all transitive dependencies
            var reuseBuilds = this.ReuseLastSuccessfulBuild ? ReuseBuilds.LastSuccessful : ReuseBuilds.Default;

            var defaultArtifactRules = $"+:{buildArtifactsDirectory}/**/*=>{buildArtifactsDirectory}";

            snapshotDependencies = declaredSnapshotDependencies
                .Select(
                    d => d.ToTeamCitySnapshotDependency(
                        d.TryGetObjectName( product )
                        ?? throw new KeyNotFoundException(
                            $"The '{this.Id}' build configuration depends on '{d}', which the product does not declare." ),
                        defaultArtifactRules,
                        this.EffectiveReuseLastSuccessfulBuild ) )
                .ToList();

            snapshotDependencies.AddRange(
                productDependencies.Select(
                    d => new TeamCitySnapshotDependency(
                        d.Definition.CiConfiguration.BuildTypes[d.Configuration],
                        true,
                        $"+:{d.Definition.GetPrivateArtifactsDirectory( d.Configuration ).Replace( Path.DirectorySeparatorChar, '/' )}/**/*=>dependencies/{d.Key}",
                        ReuseBuilds: reuseBuilds ) ) );

            // Both steps below read a file that a stage of this product published, so they belong to a configuration
            // that waits for such a stage. A configuration that takes only the artifacts of other products has no
            // such directory, and it builds the product from source, which writes both files itself.
            if ( declaredSnapshotDependencies.Length > 0 )
            {
                // If we have a build snapshot dependency, copy nuget.restored.config to nuget.config
                var copyNuGetConfigCommand =
                    $@"Copy-Item -Path ""{buildArtifactsDirectory}/nuget.restored.config"" -Destination ""nuget.config"" -Force;";

                if ( product.AddWslSupport )
                {
                    copyNuGetConfigCommand +=
                        $@"Copy-Item -Path ""{buildArtifactsDirectory}/nuget.restored.config"" -Destination ""nuget.wsl.config"" -Force;";
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

                foreach ( var dependency in productDependencies )
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
        }

        // Add the main execution step
        buildSteps.Add(
            new PowerShellScriptBuildStep(
                "Exec",
                $"Execute {this.GetScriptPath( productProperties )}",
                this.GetScriptPath( productProperties ),
                this.Arguments,
                this.BuildAgentRequirements == null
                    ? (this.Dockerfile != null && product.DockerSpec != null ? product.DockerSpec with { Dockerfile = this.Dockerfile } : product.DockerSpec)
                    : this.BuildAgentRequirements is ContainerHostRequirements containerHostRequirements
                        ? new DockerSpec(
                            $"{productProperties.Product.ProductNameWithoutDot}-{productProperties.Product.ProductFamily.Version}-{this.Id}".ToLowerInvariant(),
                            Memory: this.ContainerMemoryInGigabytes,
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
            StartsContainers = this.StartsContainers,
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
            Parameters = this.Parameters,
            TimeoutInMinutes = this.TimeoutInMinutes,
            BuildTriggers = this.BuildTriggers,

            // Escaped rather than real newlines: the generator un-escapes them into the Kotlin triple-quoted
            // string, so a rule list assembled with "\n" here would break out of it.
            ArtifactRules = this.ArtifactRules == null ? null : string.Join( @"\n", this.ArtifactRules )
        };

        return downstreamMergeConfiguration;
    }
}