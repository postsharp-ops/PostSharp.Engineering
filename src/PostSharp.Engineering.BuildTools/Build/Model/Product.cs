// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.BillOfMaterials;
using PostSharp.Engineering.BuildTools.Build.Bumping;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Publishing;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Triggers;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PostSharp.Engineering.BuildTools.Build.Model
{
    [PublicAPI]
    public class Product
    {
        public DependencyDefinition DependencyDefinition { get; }

        private readonly string? _versionsFile;
        private readonly string? _mainVersionFile;
        private readonly string? _autoUpdatedVersionsFile;

        public Product( DependencyDefinition dependencyDefinition )
        {
            this.DependencyDefinition = dependencyDefinition;
            this.ProductName = dependencyDefinition.Name;
            this.BuildExePath = Assembly.GetCallingAssembly().Location;
        }

        public bool AddDefaultCommands { get; init; } = true;

        public ProductFamily ProductFamily => this.DependencyDefinition.ProductFamily;

        public string BuildExePath { get; }

        public string EngineeringDirectory => this.DependencyDefinition.EngineeringDirectory;

        public string VersionsFilePath
        {
            get => this._versionsFile ?? Path.Combine( this.EngineeringDirectory, "Versions.props" );
            init => this._versionsFile = value;
        }

        public string MainVersionFilePath
        {
            get => this._mainVersionFile ?? Path.Combine( this.EngineeringDirectory, "MainVersion.props" );
            init => this._mainVersionFile = value;
        }

        public string AutoUpdatedVersionsFilePath
        {
            get => this._autoUpdatedVersionsFile ?? Path.Combine( this.EngineeringDirectory, AutoUpdatedVersionsFile.FileName );
            init => this._autoUpdatedVersionsFile = value;
        }

        /// <summary>
        /// Gets the dependency from which the main version should be copied.
        /// </summary>
        public DependencyDefinition? MainVersionDependency { get; init; }

        public string ProductName { get; }

        public string ProductNameWithoutDot => this.ProductName.Replace( ".", "", StringComparison.OrdinalIgnoreCase );

        [Obsolete( "Use GetPrivateArtifactsRelativeDirectory." )]
        public string PrivateArtifactsDirectory => this.DependencyDefinition.PrivateArtifactsDirectory;

        public string GetPrivateArtifactsRelativeDirectory( BuildConfiguration configuration )
            => this.DependencyDefinition.GetPrivateArtifactsDirectory( configuration );

        public string PublicArtifactsDirectory => this.DependencyDefinition.PublicArtifactsDirectory;

        public string TestResultsDirectory { get; init; } = Path.Combine( "artifacts", "testResults" );

        public string LogsDirectory { get; init; } = Path.Combine( "artifacts", "logs" );

        public string DumpDirectory { get; init; } = Path.Combine( "artifacts", "dumps" );

        public string SourceDependenciesDirectory { get; init; } = Path.Combine( "source-dependencies" );

        public bool GenerateArcadeProperties { get; init; }

        public string[] AdditionalDirectoriesToClean { get; init; } = [];

        public Solution[] Solutions { get; init; } = [];

        /// <summary>
        /// Gets the pattern selecting private artifacts for all configuration.
        /// </summary>
        /// <seealso cref="BuildConfigurationInfo.PrivateArtifacts"/>
        public Pattern PrivateArtifacts { get; init; } = Pattern.Empty;

        /// <summary>
        /// Gets the pattern selecting public artifacts for all configuration.
        /// </summary>
        /// <seealso cref="BuildConfigurationInfo.PublicArtifacts"/>
        public Pattern PublicArtifacts { get; init; } = Pattern.Empty;

        public bool KeepEditorConfig { get; init; }

        public BuildAgentRequirements? OverriddenBuildAgentRequirements { get; init; }

        public BuildAgentRequirements AdditionalBuildAgentRequirements = BuildAgentRequirements.Empty;

        public BuildAgentRequirements ResolvedBuildAgentRequirements
        {
            get
            {
                if ( this.OverriddenBuildAgentRequirements != null )
                {
                    return this.OverriddenBuildAgentRequirements;
                }
                else
                {
                    return this.ProductFamily.DefaultBuildAgentRequirements.Combine( this.AdditionalBuildAgentRequirements );
                }
            }
        }

        public ConfigurationSpecific<BuildConfigurationInfo> Configurations { get; init; } = DefaultConfigurations;

        public TimeSpan BuildTimeout
        {
            [Obsolete( "Get BuildContext.BuildTimeout." )]
            get;
            init;
        } = TimeSpan.FromMinutes( 30 );

        public TimeSpan DeploymentTimeout { get; init; } = TimeSpan.FromMinutes( 30 );

        public TimeSpan SwapTimeout { get; init; } = TimeSpan.FromMinutes( 15 );

        public TimeSpan VersionBumpTimeout { get; init; } = TimeSpan.FromMinutes( 15 );

        public TimeSpan UpstreamMergeTimeout { get; init; } = TimeSpan.FromMinutes( 15 );

        public static ImmutableArray<Publisher> DefaultPublicPublishers { get; }
            =
            [
                new NugetPublisher( Pattern.Create( "*.nupkg" ), "https://api.nuget.org/v3/index.json", $"%{EnvironmentVariableNames.NuGetOrgApiKey}%" ),
                new VsixPublisher( Pattern.Create( "*.vsix" ) )
            ];

        public static ConfigurationSpecific<BuildConfigurationInfo> DefaultConfigurations { get; }
            = new(
                debug:
                new BuildConfigurationInfo( BuildTriggers: [new SourceBuildTrigger()] ),
                release: new BuildConfigurationInfo(),
                @public: new BuildConfigurationInfo(
                    RequiresSigning: true,
                    PublicPublishers: DefaultPublicPublishers.ToArray(),
                    ExportsToTeamCityDeploy: true,
                    RequiresUpstreamCheck: true ) );

        public IEnumerable<IBuildComponent> GetBuildComponents()
        {
            HashSet<IBuildComponent> components = new();

            foreach ( var configuration in this.Configurations.All )
            {
                AddComponents( configuration.PublicPublishers );
                AddComponents( configuration.PrivatePublishers );
                AddComponents( configuration.Swappers );
            }

            return components;

            void AddComponents( IEnumerable<IBuildComponent>? newComponents )
            {
                if ( newComponents == null )
                {
                    return;
                }

                foreach ( var component in newComponents )
                {
                    if ( components.Add( component ) )
                    {
                        AddComponents( component.Children );
                    }
                }
            }
        }

        public ImmutableArray<string> DefaultArtifactRules { get; } = ImmutableArray<string>.Empty;

        /// <summary>
        /// List of properties that must be exported into the *.version.props. These properties must be defined in *.props files specified as the dictionary keys.
        /// </summary>
        public Dictionary<string, string[]> ExportedProperties { get; init; } = new();

        /// <summary>
        /// Gets the set of artifact dependencies of this product given their <see cref="ParametrizedDependency"/>.
        /// </summary>
        [PublicAPI]
        public ParametrizedDependency[] ParametrizedDependencies => this.DependencyDefinition.Dependencies;

        /// <summary>
        /// Gets the set of source code dependencies of this product. 
        /// </summary>
        public DependencyDefinition[] SourceDependencies => this.DependencyDefinition.SourceDependencies;

        public IBumpStrategy BumpStrategy { get; init; } = new DefaultBumpStrategy();

        internal bool UseDocker => this.ResolvedBuildAgentRequirements.IsDockerized;

        /// <summary>
        /// Gets or sets a value indicating whether WSL support should be enabled.
        /// When true, generates WSL-compatible version files (.wsl.g.props) and nuget.wsl.config.
        /// </summary>
        [PublicAPI]
        public bool AddWslSupport { get; init; }

        public DockerSpec? DockerSpec
            => this.ResolvedBuildAgentRequirements is ContainerHostRequirements containerHostRequirements
                ? new DockerSpec( $"{this.ProductNameWithoutDot}-{this.ProductFamily.Version}".ToLowerInvariant() )
                : null;

        public bool IsPublishingNonReleaseBranchesAllowed { get; init; }

        /// <summary>
        /// Gets or sets values that override the values found on nuget.org for the packages.
        /// Used to construct the SBOM.
        /// </summary>
        public DependentPackageInfoOverride[] DependentPackageInfoOverrides { get; init; } = [];

        /// <summary>
        /// Gets or sets the list of packages that should be excluded from the SBOM.
        /// </summary>
        public DependentPackageExclusion[] DependentPackageExclusions { get; init; } = [];

        /// <summary>
        /// Gets or sets a list of specifications about how projects will be consumed. Used to construct the SBOM.
        /// </summary>
        public ProjectUsageInfo[] ProjectUsages { get; init; } = [];

        /// <summary>
        /// Gets or sets a globbing pattern capturing the list of <c>*.deps.json</c> for projects that must be included in the SBOM.
        /// The pattern is appended to the default pattern.
        /// </summary>
        public Pattern ConsumableDepsFiles { get; init; } = Pattern.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the <c>generate-scripts</c> command should the TeamCity setting scripts.
        /// </summary>
        public bool GenerateTeamCitySettings { get; init; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the <c>prepare</c> command should generate the <c>nuget.config</c> file.
        /// </summary>
        public bool GenerateNuGetConfig { get; init; }

        /// <summary>
        /// Gets or sets the content of the <c>global.json</c> file, if it must be generated by the <c>prepare</c> command. If this property is <c>null</c>,
        /// the <c>global.json</c> file is not generated.
        /// </summary>
        public DotNetSdkVersion? DotNetSdkVersion { get; init; }

        /// <summary>
        /// Gets or sets the version of MSBuild used by <see cref="PostSharp.Engineering.BuildTools.Build.Solutions.MsbuildSolution"/>.
        /// The highest version that matches the specified version components of this property is chosen. If this property is not set,
        /// MSBuild cannot be used.
        /// </summary>
        public Version? MSBuildVersion { get; init; }

        public AdditionalCiBuildConfiguration[] AdditionalCiBuildConfigurations { get; init; } = [];

        public AdditionalDockerfile[] AdditionalDockerfiles { get; init; } = [];

        public bool TryGetDependency( string name, [NotNullWhen( true )] out ParametrizedDependency? dependency )
        {
            dependency = this.ParametrizedDependencies.SingleOrDefault( d => d.Name == name );

            // We do NOT attempt to get a ParametrizedDependency from a DependencyDefinition because we basically
            // don't know what the parameters are, and returning default parameters may delay the moment when a design
            // issue is visible.

            return dependency != null;
        }

        public DependencyDefinition GetDependencyDefinition( string name )
        {
            if ( !this.TryGetDependencyDefinition( name, out var definition ) )
            {
                throw new KeyNotFoundException( $"Dependency not found: {name}." );
            }

            return definition;
        }

        public bool TryGetDependencyDefinition( string name, [NotNullWhen( true )] out DependencyDefinition? dependencyDefinition )
        {
            dependencyDefinition = this.ParametrizedDependencies.SingleOrDefault( d => d.Name == name )?.Definition;

            if ( dependencyDefinition != null )
            {
                return true;
            }
            else
            {
                return this.ProductFamily.TryGetDependencyDefinition( name, out dependencyDefinition );
            }
        }

        public Dictionary<string, string> SupportedProperties { get; init; } = new();

        public bool RequiresEngineeringSdk { get; init; } = true;

        public ImmutableArray<DotNetTool> DotNetTools { get; init; } = DotNetTool.DefaultTools;

        public bool TestOnBuild { get; init; }

        public string? DefaultTestsFilter { get; init; }

        public ProductExtension[] Extensions { get; init; } = [];

        public bool BuildRequiresSourceDependencies { get; init; } = true;

        internal string GetPrivateArtifactsAbsoluteDirectory( BuildContext context, BuildConfiguration configuration )
            => Path.Combine(
                context.RepoDirectory,
                this.GetPrivateArtifactsRelativeDirectory( configuration ) );

        internal string GetPublicArtifactsAbsoluteDirectory( BuildContext context )
            => Path.Combine(
                context.RepoDirectory,
                this.PublicArtifactsDirectory );

        /// <summary>
        /// An event raised when the build is completed, before creating ZIP files and preparing public artifacts.
        /// </summary>
        public event Action<BuildCompletedEventArgs>? BuildCompleted;

        internal void OnBuildCompleted( BuildCompletedEventArgs args ) => this.BuildCompleted?.Invoke( args );

        /// <summary>
        /// An event raised when the build is completed, after creating ZIP files and preparing public artifacts.
        /// </summary>
        public event Action<BuildCompletedEventArgs>? ArtifactsPrepared;

        internal void OnArtifactsPrepared( BuildCompletedEventArgs args ) => this.ArtifactsPrepared?.Invoke( args );

        /// <summary>
        /// An event raised when the tests runs are completed.
        /// </summary>
        public event Action<BuildCompletedEventArgs>? TestCompleted;

        internal void OnTestCompleted( BuildCompletedEventArgs args ) => this.TestCompleted?.Invoke( args );

        /// <summary>
        /// An event raised when the Prepare phase is complete.
        /// </summary>
        public event Action<PrepareCompletedEventArgs>? PrepareCompleted;

        internal void OnPrepareCompleted( PrepareCompletedEventArgs args )
        {
            this.PrepareCompleted?.Invoke( args );
        }

        internal (string Private, string Public) GetArtifactsAbsoluteDirectories( BuildContext context, BuildConfiguration configuration )
        {
            return (
                Path.Combine( context.RepoDirectory, this.GetPrivateArtifactsRelativeDirectory( configuration ) ),
                Path.Combine( context.RepoDirectory, this.PublicArtifactsDirectory )
            );
        }
    }
}