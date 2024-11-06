// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.FileSystemGlobbing;
using PostSharp.Engineering.BuildTools.Build.Publishers;
using PostSharp.Engineering.BuildTools.Build.Triggers;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;
using PostSharp.Engineering.BuildTools.Coverage;
using PostSharp.Engineering.BuildTools.Dependencies;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Git;
using PostSharp.Engineering.BuildTools.NuGet;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Model
{
    [PublicAPI]
    public class Product
    {
        public DependencyDefinition DependencyDefinition { get; }

        private readonly string? _versionsFile;
        private readonly string? _mainVersionFile;
        private readonly string? _autoUpdatedVersionsFile;
        private readonly string? _bumpInfoFile;
        private readonly ParametrizedDependency[] _parametrizedDependencies = [];
        private readonly DependencyDefinition[] _dependencyDefinitions = [];

        public Product( DependencyDefinition dependencyDefinition )
        {
            this.DependencyDefinition = dependencyDefinition;
            this.ProductName = dependencyDefinition.Name;
            this.BuildExePath = Assembly.GetCallingAssembly().Location;
            this.DockerBaseImage = dependencyDefinition.ProductFamily.DockerBaseImage;
        }

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
            get => this._autoUpdatedVersionsFile ?? Path.Combine( this.EngineeringDirectory, "AutoUpdatedVersions.props" );
            init => this._autoUpdatedVersionsFile = value;
        }

        public string BumpInfoFilePath
        {
            get => this._bumpInfoFile ?? Path.Combine( this.EngineeringDirectory, "BumpInfo.txt" );
            init => this._bumpInfoFile = value;
        }

        /// <summary>
        /// Gets the dependency from which the main version should be copied.
        /// </summary>
        public DependencyDefinition? MainVersionDependency { get; init; }

        public string ProductName { get; }

        public string ProductNameWithoutDot => this.ProductName.Replace( ".", "", StringComparison.OrdinalIgnoreCase );

        public ParametricString PrivateArtifactsDirectory => this.DependencyDefinition.PrivateArtifactsDirectory;

        public ParametricString PublicArtifactsDirectory => this.DependencyDefinition.PublicArtifactsDirectory;

        public ParametricString TestResultsDirectory { get; init; } = Path.Combine( "artifacts", "testResults" );

        public ParametricString LogsDirectory { get; init; } = Path.Combine( "artifacts", "logs" );

        public ParametricString SourceDependenciesDirectory { get; init; } = Path.Combine( "source-dependencies" );

        public bool GenerateArcadeProperties { get; init; }

        public string[] AdditionalDirectoriesToClean { get; init; } = [];

        public Solution[] Solutions { get; init; } = [];

        public Pattern PrivateArtifacts { get; init; } = Pattern.Empty;

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
                else if ( this.UseDockerInTeamcity )
                {
                    return this.DockerBaseImage!.HostRequirements.Combine( this.AdditionalBuildAgentRequirements );
                }
                else
                {
                    return this.ProductFamily.DefaultBuildAgentRequirements.Combine( this.AdditionalBuildAgentRequirements );
                }
            }
        }

        public ConfigurationSpecific<BuildConfigurationInfo> Configurations { get; init; } = DefaultConfigurations;

        public TimeSpan BuildTimeOutThreshold { get; init; } = TimeSpan.FromMinutes( 5 );

        public TimeSpan DeploymentTimeOutThreshold { get; init; } = TimeSpan.FromMinutes( 5 );

        public TimeSpan SwapTimeOutThreshold { get; init; } = TimeSpan.FromMinutes( 5 );

        public TimeSpan VersionBumpTimeOutThreshold { get; init; } = TimeSpan.FromMinutes( 5 );

        public TimeSpan DownstreamMergeTimeOutThreshold { get; init; } = TimeSpan.FromMinutes( 5 );

        public static ImmutableArray<Publisher> DefaultPublicPublishers { get; }
            =
            [
                ..new Publisher[]
                {
                    // .snupkg packages are published along with .nupkg packages automatically by the "dotnet nuget push" tool.
                    new NugetPublisher( Pattern.Create( "*.nupkg" ), "https://api.nuget.org/v3/index.json", "%NUGET_ORG_API_KEY%" ),
                    new VsixPublisher( Pattern.Create( "*.vsix" ) )
                }
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

        public ImmutableArray<string> DefaultArtifactRules { get; } = ImmutableArray<string>.Empty;

        /// <summary>
        /// List of properties that must be exported into the *.version.props. These properties must be defined in *.props files specified as the dictionary keys.
        /// </summary>
        public Dictionary<string, string[]> ExportedProperties { get; init; } = new();

        /// <summary>
        /// Gets the set of artifact dependencies of this product given their <see cref="DependencyDefinition"/>.
        /// When at least one dependency requires to override some parameter from its defaults, use the <see cref="ParametrizedDependencies"/> property.
        /// </summary>
        [PublicAPI]
        public DependencyDefinition[] Dependencies
        {
            [Obsolete( "Use CustomizedDependencies" )]
            get => this._dependencyDefinitions;
            init => this.ParametrizedDependencies = value.Select( x => x.ToDependency() ).ToArray();
        }

        /// <summary>
        /// Gets the set of artifact dependencies of this product given their <see cref="ParametrizedDependency"/>.
        /// </summary>
        [PublicAPI]
        public ParametrizedDependency[] ParametrizedDependencies
        {
            get => this._parametrizedDependencies;

            init
            {
                this._parametrizedDependencies = value;
                this._dependencyDefinitions = value.Select( x => x.Definition ).ToArray();
            }
        }

        /// <summary>
        /// Gets the set of source code dependencies of this product. 
        /// </summary>
        public DependencyDefinition[] SourceDependencies { get; init; } = [];

        public IBumpStrategy BumpStrategy { get; init; } = new DefaultBumpStrategy();

        public DockerImage? DockerBaseImage { get; init; }

        public DockerImageComponent[] AdditionalDockerImageComponents { get; init; } = [];

        public bool UseDockerInTeamcity { get; init; }
        
        public bool IsPublishingNonReleaseBranchesAllowed { get; init; }

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

        public bool IsBundle { get; init; }

        private string GetPrivateArtifactsDirectory( BuildContext context, BuildInfo buildInfo )
            => Path.Combine(
                context.RepoDirectory,
                this.PrivateArtifactsDirectory.ToString( buildInfo ) );

        private string GetPublicArtifactsDirectory( BuildContext context, BuildInfo buildInfo )
            => Path.Combine(
                context.RepoDirectory,
                this.PublicArtifactsDirectory.ToString( buildInfo ) );

        public bool Build( BuildContext context, BuildSettings settings )
        {
            var configuration = settings.BuildConfiguration;
            var buildConfigurationInfo = this.Configurations[configuration];

            // Skip if we have a date tag and a fresh build.
            DateTime dateTag;

            if ( settings.DateTag != null )
            {
                dateTag = DateTime.FromBinary( settings.DateTag.Value );
                var propsFile = this.GetVersionPropertiesFilePath( context, settings );

                if ( !File.Exists( propsFile ) || File.GetLastWriteTime( propsFile ) > dateTag )
                {
                    context.Console.WriteMessage( "There is already a fresh build." );

                    return true;
                }
            }
            else
            {
                dateTag = DateTime.Now;
            }

            // Build dependencies.
            DependenciesOverrideFile? dependenciesOverrideFile;

            if ( !settings.NoDependencies )
            {
                if ( !this.Prepare( context, settings, out dependenciesOverrideFile ) )
                {
                    return false;
                }
            }
            else
            {
                // Read the resolved dependencies.
                if ( !DependenciesOverrideFile.TryLoad( context, settings, configuration, out dependenciesOverrideFile ) )
                {
                    return false;
                }
            }

            // If we have a recursive build, build local dependencies.
            if ( settings.Recursive )
            {
                foreach ( var dependency in dependenciesOverrideFile.Dependencies )
                {
                    if ( dependency.Value.SourceKind == DependencySourceKind.Local )
                    {
                        if ( context.Product.TryGetDependencyDefinition( dependency.Key, out var dependencyDefinition )
                             && dependencyDefinition.ExcludeFromRecursiveBuild )
                        {
                            continue;
                        }

                        context.Console.WriteHeading( $"Build dependency {dependency.Key}" );

                        var dependencyDirectory = Path.GetDirectoryName( dependency.Value.VersionFile! )!;

                        var buildFile = Path.Combine( dependencyDirectory, "Build.ps1" );

                        if ( !File.Exists( buildFile ) )
                        {
                            context.Console.WriteError( $"Cannot find '{buildFile}'." );

                            return false;
                        }

                        if ( !ToolInvocationHelper.InvokePowershell(
                                context.Console,
                                buildFile,
                                $"build --recursive --if-older={dateTag.ToBinary()} -c {settings.BuildConfiguration.ToString().ToLowerInvariant()} --nologo",
                                dependencyDirectory ) )
                        {
                            context.Console.WriteError( $"Cannot build the dependency {dependency.Key}." );

                            return false;
                        }
                    }
                }
            }

            // Delete the root import file in the repo because the presence of this file means a successful build.
            this.DeleteImportFile( context );

            // We have to read the version from the file we have generated - using MSBuild, because it contains properties.
            var buildInfo = this.ReadGeneratedVersionFile( context.GetManifestFilePath( configuration ) );

            var privateArtifactsDirectory = this.GetPrivateArtifactsDirectory( context, buildInfo );

            // Build.
            if ( !this.BuildCore( context, settings ) )
            {
                return false;
            }

            var publicArtifactsDirectory = this.GetPublicArtifactsDirectory( context, buildInfo );

            // Allow for some customization before we create the zip file and copy to the public directory.
            var eventArgs = new BuildCompletedEventArgs( context, settings, buildInfo, privateArtifactsDirectory, publicArtifactsDirectory );
            this.BuildCompleted?.Invoke( eventArgs );

            if ( eventArgs.IsFailed )
            {
                return false;
            }

            // Check that the build produced the expected artifacts.
            var allFilesPattern = this.PublicArtifacts.Append( this.PrivateArtifacts );

            if ( !allFilesPattern.Verify( context, privateArtifactsDirectory, buildInfo ) )
            {
                return false;
            }

            // Zipping internal artifacts.
            void CreateZip( string directory )
            {
                if ( settings.CreateZip )
                {
                    var zipFile = Path.Combine( directory, $"{this.ProductName}-{buildInfo.PackageVersion}.zip" );

                    context.Console.WriteMessage( $"Creating '{zipFile}'." );
                    var tempFile = Path.Combine( Path.GetTempPath(), Guid.NewGuid() + ".zip" );

                    ZipFile.CreateFromDirectory(
                        directory,
                        tempFile,
                        CompressionLevel.Optimal,
                        false );

                    File.Move( tempFile, zipFile );
                }
            }

            CreateZip( privateArtifactsDirectory );

            // Copy public artifacts to the publish directory.
            if ( !Directory.Exists( publicArtifactsDirectory ) )
            {
                Directory.CreateDirectory( publicArtifactsDirectory );
            }

            void CreateEmptyPublicDirectory()
            {
                // We have to create an empty file, otherwise TeamCity will complain that
                // artifacts are missing.
                var emptyFile = Path.Combine( publicArtifactsDirectory, ".empty" );

                File.WriteAllText( emptyFile, "This file is intentionally empty." );
            }

            if ( this.PublicArtifacts.IsEmpty )
            {
                context.Console.WriteMessage( "Do not prepare public artifacts because there is none." );
                CreateEmptyPublicDirectory();
            }
            else if ( settings.BuildConfiguration != BuildConfiguration.Public )
            {
                context.Console.WriteMessage( "Do not prepare public artifacts because this is not a public build" );
                CreateEmptyPublicDirectory();
            }
            else
            {
                // Copy artifacts.
                context.Console.WriteHeading( "Copying public artifacts" );
                var filePatternMatches = new List<FilePatternMatch>();

                this.PublicArtifacts.TryGetFiles( privateArtifactsDirectory, buildInfo, filePatternMatches );
                IEnumerable<string> files = filePatternMatches.Select( m => m.Path ).ToArray();

                // Automatically include respective symbol NuGet packages.
                files = files.Concat(
                    files.Where( f => f.EndsWith( ".nupkg", StringComparison.OrdinalIgnoreCase ) )
                        .Select( f => f[..^".nupkg".Length] + ".snupkg" )
                        .Where( f => File.Exists( Path.Combine( privateArtifactsDirectory, f ) ) ) );

                foreach ( var file in files )
                {
                    var targetFile = Path.Combine( publicArtifactsDirectory, Path.GetFileName( file ) );

                    context.Console.WriteMessage( file );
                    File.Copy( Path.Combine( privateArtifactsDirectory, file ), targetFile, true );
                }

                var signSuccess = true;

                if ( buildConfigurationInfo.RequiresSigning && !settings.NoSign )
                {
                    context.Console.WriteHeading( "Signing artifacts" );

                    var signToolSecret = Environment.GetEnvironmentVariable( "SIGNSERVER_SECRET" );

                    if ( signToolSecret == null )
                    {
                        context.Console.WriteError( "The SIGNSERVER_SECRET environment variable is not defined." );

                        return false;
                    }

                    void Sign( string filter )
                    {
                        if ( Directory.EnumerateFiles( publicArtifactsDirectory, filter ).Any() )
                        {
                            signSuccess = signSuccess && DotNetTool.SignClient.Invoke(
                                context,
                                $"Sign --baseDirectory \"{publicArtifactsDirectory}\" --input {filter}" );
                        }
                    }

                    Sign( "*.nupkg" );
                    Sign( "*.snupkg" );
                    Sign( "*.vsix" );

                    if ( !signSuccess )
                    {
                        return false;
                    }

                    // Zipping public artifacts.
                    CreateZip( publicArtifactsDirectory );

                    context.Console.WriteSuccess( "Signing artifacts was successful." );
                }
            }

            // Create the consolidate directory.
            if ( settings.CreateConsolidatedDirectory )
            {
                context.Console.WriteHeading( "Creating the consolidated directory" );

                var consolidatedDirectory = Path.Combine(
                    context.RepoDirectory,
                    "artifacts",
                    "consolidated",
                    configuration.ToString().ToLowerInvariant() );

                if ( Directory.Exists( consolidatedDirectory ) )
                {
                    Directory.Delete( consolidatedDirectory, true );
                }

                Directory.CreateDirectory( consolidatedDirectory );

                context.Console.WriteMessage( $"Creating '{consolidatedDirectory}'." );

                // Copy dependencies.

                foreach ( var dependency in dependenciesOverrideFile.Dependencies )
                {
                    if ( dependency.Value.VersionFile != null )
                    {
                        var versionDocument = XDocument.Load( dependency.Value.VersionFile );
                        var import = versionDocument.Root!.Element( "Import" )?.Attribute( "Project" )?.Value;

                        string importDirectory;

                        if ( import == null )
                        {
                            importDirectory = Path.GetDirectoryName( dependency.Value.VersionFile )!;
                        }
                        else
                        {
                            importDirectory = Path.GetDirectoryName( Path.Combine( Path.GetDirectoryName( dependency.Value.VersionFile )!, import ) )!;
                        }

                        CopyPackages( importDirectory );
                    }
                }

                // Copy current repo.
                CopyPackages( privateArtifactsDirectory );

                void CopyPackages( string directory )
                {
                    foreach ( var file in Directory.GetFiles( directory, "*.nupkg" ).Concat( Directory.GetFiles( directory, "*.snupkg" ) ) )
                    {
                        File.Copy( file, Path.Combine( consolidatedDirectory, Path.GetFileName( file ) ), true );
                    }
                }
            }

            // Writing the import file at the end of the build so it gets only written if the build was successful.
            this.WriteImportFile( context, configuration );

            this.ArtifactsPrepared?.Invoke( eventArgs );

            context.Console.WriteSuccess( $"Building the whole {this.ProductName} product was successful. Package version: {buildInfo.PackageVersion}." );

            return true;
        }

        private void DeleteImportFile( BuildContext context )
        {
            var importFilePath = Path.Combine( context.RepoDirectory, this.ProductName + ".Import.props" );

            if ( File.Exists( importFilePath ) )
            {
                File.Delete( importFilePath );
            }
        }

        private void WriteImportFile( BuildContext context, BuildConfiguration configuration )
        {
            // Write a link to this file in the root file of the repo. This file is the interface of the repo, which can be imported by other repos.
            var manifestFilePath = context.GetManifestFilePath( configuration );
            var importFilePath = Path.Combine( context.RepoDirectory, this.ProductName + ".Import.props" );

            // We're generating a relative path so that the path can be resolved even when the filesystem is mounted
            // to a different location than the current one (used e.g. when using Hyper-V).
            var relativePath = Path.GetRelativePath( Path.GetDirectoryName( importFilePath )!, manifestFilePath );

            var importFileContent = $@"
<!-- File generated by PostSharp.Engineering {VersionHelper.EngineeringVersion}, method {nameof(Product)}.{nameof(this.WriteImportFile)} -->
<Project>
    <!-- This file must not be added to source control and must not be uploaded as a build artifact.
         It must be imported by other repos as a dependency. 
         Dependent projects should not directly reference the artifacts path, which is considered an implementation detail. -->
    <Import Project=""{relativePath}""/>
</Project>
";

            File.WriteAllText( importFilePath, importFileContent );
        }

        private BuildInfo ReadGeneratedVersionFile( string path )
        {
            var versionFilePath = path;
            var versionFile = Project.FromFile( versionFilePath, new ProjectOptions() );

            string packageVersion;

            if ( this.GenerateArcadeProperties )
            {
                packageVersion = versionFile.Properties
                    .Single( p => p.Name == this.ProductNameWithoutDot + "VersionPrefix" )
                    .EvaluatedValue;

                var suffix = versionFile
                    .Properties
                    .Single( p => p.Name == this.ProductNameWithoutDot + "VersionSuffix" )
                    .EvaluatedValue;

                if ( !string.IsNullOrWhiteSpace( suffix ) )
                {
                    packageVersion = packageVersion + "-" + suffix;
                }
            }
            else
            {
                packageVersion = versionFile
                    .Properties
                    .Single( p => p.Name == this.ProductNameWithoutDot + "Version" )
                    .EvaluatedValue;
            }

            if ( string.IsNullOrEmpty( packageVersion ) )
            {
                throw new InvalidOperationException( "PackageVersion should not be null." );
            }

            var configuration = versionFile
                .Properties
                .Single( p => p.Name == this.ProductNameWithoutDot + "BuildConfiguration" )
                .EvaluatedValue;

            var packagePreviewVersion = versionFile
                .Properties
                .Single( p => p.Name == this.ProductNameWithoutDot + "PreviewVersion" )
                .EvaluatedValue;

            if ( string.IsNullOrEmpty( configuration ) )
            {
                throw new InvalidOperationException( "BuildConfiguration should not be null." );
            }

            ProjectCollection.GlobalProjectCollection.UnloadAllProjects();

            return new BuildInfo( packageVersion, Enum.Parse<BuildConfiguration>( configuration ), this, packagePreviewVersion );
        }

        public record MainVersionFileInfo(
            string MainVersion,
            string? OverriddenPatchVersion,
            string PackageVersionSuffix,
            int? OurPatchVersion )
        {
            public string Release => new Version( this.MainVersion ).ToString( 2 );
        }

        /// <summary>
        /// Reads MainVersion.props but does not interpret anything.
        /// </summary>
        public bool TryReadMainVersionFile(
            BuildContext context,
            [NotNullWhen( true )] out MainVersionFileInfo? mainVersionFileInfo )
            => this.TryReadMainVersionFile( context, out mainVersionFileInfo, out _ );
        
        /// <summary>
        /// Reads MainVersion.props but does not interpret anything.
        /// </summary>
        public bool TryReadMainVersionFile(
            BuildContext context,
            [NotNullWhen( true )] out MainVersionFileInfo? mainVersionFileInfo,
            out string mainVersionFilePath )
        {
            mainVersionFileInfo = null;

            mainVersionFilePath = Path.Combine(
                context.RepoDirectory,
                this.MainVersionFilePath );

            if ( !File.Exists( mainVersionFilePath ) )
            {
                context.Console.WriteError( $"The file '{mainVersionFilePath}' does not exist." );

                return false;
            }

            var versionFile = Project.FromFile( mainVersionFilePath, new ProjectOptions() );

            var mainVersion = versionFile
                .Properties
                .SingleOrDefault( p => p.Name == "MainVersion" )
                ?.EvaluatedValue;

            var overriddenPatchVersion = versionFile
                .Properties
                .SingleOrDefault( p => p.Name == "OverriddenPatchVersion" )
                ?.EvaluatedValue;

            var ourPatchVersion = versionFile
                .Properties
                .SingleOrDefault( p => p.Name == "OurPatchVersion" )
                ?.EvaluatedValue;

            if ( string.IsNullOrEmpty( mainVersion ) )
            {
                context.Console.WriteError( $"MainVersion should not be null in '{mainVersionFilePath}'." );

                return false;
            }

            var suffix = versionFile
                             .Properties
                             .SingleOrDefault( p => p.Name == "PackageVersionSuffix" )
                             ?.EvaluatedValue
                         ?? "";

            // Empty suffixes are allowed and mean RTM.

            ProjectCollection.GlobalProjectCollection.UnloadAllProjects();

            mainVersionFileInfo = new(
                mainVersion,
                overriddenPatchVersion,
                suffix,
                ourPatchVersion != null ? int.Parse( ourPatchVersion, CultureInfo.InvariantCulture ) : null );

            return true;
        }

        /// <summary>
        /// An event raised when the build is completed, before creating ZIP files and preparing public artifacts.
        /// </summary>
        public event Action<BuildCompletedEventArgs>? BuildCompleted;

        /// <summary>
        /// An event raised when the build is completed, after creating ZIP files and preparing public artifacts.
        /// </summary>
        public event Action<BuildCompletedEventArgs>? ArtifactsPrepared;

        /// <summary>
        /// An event raised when the tests runs are completed.
        /// </summary>
        public event Action<BuildCompletedEventArgs>? TestCompleted;

        /// <summary>
        /// An event raised when the Prepare phase is complete.
        /// </summary>
        public event Action<PrepareCompletedEventArgs>? PrepareCompleted;

        protected virtual bool BuildCore( BuildContext context, BuildSettings settings )
        {
            IEnumerable<Solution> solutionsToBuild;

            if ( settings.SolutionId != null )
            {
                var solution = this.Solutions[settings.SolutionId.Value - 1];
                solutionsToBuild = new[] { solution };
            }
            else
            {
                solutionsToBuild = this.Solutions;
            }

            foreach ( var solution in solutionsToBuild )
            {
                if ( settings.IncludeTests || !solution.IsTestOnly )
                {
                    context.Console.WriteHeading( $"Building {solution.Name} ({settings.BuildConfiguration} configuration)" );

                    if ( !settings.NoDependencies )
                    {
                        if ( !solution.Restore( context, settings ) )
                        {
                            return false;
                        }
                    }

                    var buildMethod = solution.GetBuildMethod();

                    if ( !TryExecuteBuildMethod( context, settings, solution, buildMethod ) )
                    {
                        return false;
                    }

                    context.Console.WriteSuccess( $"Building {solution.Name} was successful." );
                }
            }

            return true;
        }

        private static bool TryExecuteBuildMethod( BuildContext context, BuildSettings settings, Solution solution, BuildMethod buildMethod )
        {
            switch ( buildMethod )
            {
                case BuildMethod.None:
                    return true;

                case BuildMethod.Build:
                    return solution.Build( context, settings );

                case BuildMethod.Pack:
                    if ( solution.PackRequiresExplicitBuild && !settings.NoDependencies )
                    {
                        if ( !solution.Build( context, settings ) )
                        {
                            return false;
                        }
                    }

                    return solution.Pack( context, settings );

                case BuildMethod.Test:
                    return solution.Test( context, settings );

                default:
                    throw new NotImplementedException( $"Build method '{buildMethod}' is not implemented." );
            }
        }

        public bool Test( BuildContext context, BuildSettings settings )
        {
            if ( !settings.NoDependencies && !this.Build( context, settings ) )
            {
                return false;
            }

            ImmutableDictionary<string, string> properties;
            var testResultsDir = Path.Combine( context.RepoDirectory, "TestResults" );

            if ( settings.AnalyzeCoverage )
            {
                // Removing the TestResults directory so that we reset the code coverage information.
                if ( Directory.Exists( testResultsDir ) )
                {
                    Directory.Delete( testResultsDir, true );
                }

                properties = settings.AnalyzeCoverage
                    ? ImmutableDictionary.Create<string, string>()
                        .Add( "CollectCoverage", "True" )
                        .Add( "CoverletOutput", testResultsDir + Path.DirectorySeparatorChar )
                    : ImmutableDictionary<string, string>.Empty;
            }
            else
            {
                properties = ImmutableDictionary<string, string>.Empty;
            }

            Solution[] solutionsToTest;

            if ( settings.SolutionId != null )
            {
                var solution = this.Solutions[settings.SolutionId.Value - 1];
                solutionsToTest = [solution];
            }
            else
            {
                solutionsToTest = this.Solutions;
            }

            if ( settings.TestsFilter == null && this.DefaultTestsFilter != null )
            {
                settings = settings.WithTestsFilter( this.DefaultTestsFilter );
            }

            foreach ( var solution in solutionsToTest )
            {
                var solutionSettings = settings;

                if ( settings.AnalyzeCoverage && solution.SupportsTestCoverage )
                {
                    solutionSettings = settings.WithAdditionalProperties( properties.ToImmutableDictionary() ).WithoutConcurrency();
                }

                context.Console.WriteHeading( $"Testing {solution.Name}." );

                if ( !TryExecuteBuildMethod( context, solutionSettings, solution, solution.TestMethod ?? BuildMethod.Test ) )
                {
                    return false;
                }

                context.Console.WriteSuccess( $"Testing {solution.Name} was successful" );
            }

            if ( settings.AnalyzeCoverage )
            {
                if ( !AnalyzeCoverageCommand.Execute(
                        context.Console,
                        new AnalyzeCoverageCommandSettings { Path = Path.Combine( testResultsDir, "coverage.net5.0.json" ) } ) )
                {
                    return false;
                }
            }

            var testResultsDirectory = Path.Combine( context.RepoDirectory, this.TestResultsDirectory.ToString() );

            if ( !Directory.Exists( testResultsDirectory ) )
            {
                Directory.CreateDirectory( testResultsDirectory );
            }

            if ( !Directory.GetFiles( testResultsDirectory ).Any() )
            {
                // We have to create an empty file, otherwise TeamCity will complain that
                // artifacts are missing.
                var emptyFile = Path.Combine( testResultsDirectory, ".empty" );

                File.WriteAllText( emptyFile, "This file is intentionally empty." );
            }

            // Raise the post-test event.
            if ( this.TestCompleted != null )
            {
                var buildInfo = this.ReadGeneratedVersionFile( context.GetManifestFilePath( settings.BuildConfiguration ) );
                var privateArtifactsDirectory = this.GetPrivateArtifactsDirectory( context, buildInfo );
                var publicArtifactsDirectory = this.GetPublicArtifactsDirectory( context, buildInfo );

                var eventArgs = new BuildCompletedEventArgs( context, settings, buildInfo, privateArtifactsDirectory, publicArtifactsDirectory );
                this.TestCompleted?.Invoke( eventArgs );
            }

            context.Console.WriteSuccess( $"Testing {this.ProductName} was successful" );

            return true;
        }

        public string GetConfigurationNeutralVersionsFilePath( BuildContext context )
            => Path.Combine( context.RepoDirectory, this.EngineeringDirectory, "Versions.g.props" );

        public string GetConfigurationSpecificVersionsFilePath( BuildContext context, CommonCommandSettings settings, BuildConfiguration configuration )
            => Path.Combine(
                context.RepoDirectory,
                this.EngineeringDirectory,
                $"Versions.{configuration}.{(TeamCityHelper.IsTeamCityBuild( settings ) ? "ci." : "")}g.props" );

        public BuildConfiguration? ReadDefaultConfiguration( BuildContext context )
        {
            var path = this.GetConfigurationNeutralVersionsFilePath( context );

            if ( !File.Exists( path ) )
            {
                return null;
            }

            var versionFile = Project.FromFile( path, new ProjectOptions() );

            var configuration = versionFile.Properties.SingleOrDefault( p => p.Name == "EngineeringConfiguration" )
                ?.UnevaluatedValue;

            if ( configuration == null )
            {
                return null;
            }

            // Note that the version suffix is not copied from the dependency, only the main version. 

            ProjectCollection.GlobalProjectCollection.UnloadAllProjects();

            return Enum.Parse<BuildConfiguration>( configuration );
        }

        public bool Prepare( BuildContext context, BuildSettings settings ) => this.Prepare( context, settings, out _ );

        public bool Prepare( BuildContext context, BuildSettings settings, [NotNullWhen( true )] out DependenciesOverrideFile? dependenciesOverrideFile )
        {
            if ( !settings.NoDependencies )
            {
                this.Clean( context, settings );
            }

            if ( settings.BuildConfiguration == BuildConfiguration.Public && !TeamCityHelper.IsTeamCityBuild( settings ) && !settings.Force )
            {
                context.Console.WriteError(
                    "Cannot prepare a public configuration on a local machine without --force because it may corrupt the package cache." );

                dependenciesOverrideFile = null;

                return false;
            }

            // Prepare the versions file.
            if ( !this.PrepareVersionsFile( context, settings, out dependenciesOverrideFile ) )
            {
                return false;
            }

            // Restore source dependencies.
            if ( this.SourceDependencies.Length > 0 )
            {
                if ( !this.RestoreSourceDependencies( context ) )
                {
                    return false;
                }
            }

            // Execute the event.
            if ( this.PrepareCompleted != null )
            {
                var eventArgs = new PrepareCompletedEventArgs( context, settings );
                this.PrepareCompleted( eventArgs );

                if ( eventArgs.IsFailed )
                {
                    return false;
                }
            }

            context.Console.WriteSuccess(
                $"Preparing the build was successful. {this.ProductNameWithoutDot}Version={this.ReadGeneratedVersionFile( context.GetManifestFilePath( settings.BuildConfiguration ) ).PackageVersion}" );

            return true;
        }

        private bool RestoreSourceDependencies( BuildContext context )
        {
            var sourceDependenciesDirectory = Path.Combine( context.RepoDirectory, "source-dependencies" );

            if ( !Directory.Exists( sourceDependenciesDirectory ) )
            {
                Directory.CreateDirectory( sourceDependenciesDirectory );
            }

            foreach ( var dependency in this.SourceDependencies )
            {
                context.Console.WriteMessage( $"Restoring '{dependency.Name}' source dependency." );

                var localDirectory = Path.Combine( context.RepoDirectory, "..", dependency.Name );

                var targetDirectory = Path.Combine( sourceDependenciesDirectory, dependency.Name );

                if ( Directory.Exists( localDirectory ) )
                {
                    if ( !Directory.Exists( targetDirectory ) )
                    {
                        context.Console.WriteMessage( $"Creating symbolic link to '{localDirectory}' in '{targetDirectory}'." );
                        Directory.CreateSymbolicLink( targetDirectory, localDirectory );

                        if ( !Directory.Exists( targetDirectory ) )
                        {
                            context.Console.WriteError( $"Symbolic link was not created for '{targetDirectory}'." );

                            return false;
                        }
                    }
                    else
                    {
                        context.Console.WriteMessage( $"Directory '{targetDirectory}' already exists." );
                    }
                }
                else
                {
                    if ( !Directory.Exists( targetDirectory ) )
                    {
                        // If the target directory doesn't exist, we clone it to the source-dependencies directory with depth of 1 to mitigate the impact of cloning the whole history.
                        if ( !ToolInvocationHelper.InvokeTool(
                                context.Console,
                                "git",
                                $"clone {dependency.VcsRepository.DeveloperMachineRemoteUrl} --branch {dependency.Branch} --depth 1",
                                sourceDependenciesDirectory ) )
                        {
                            return false;
                        }
                    }
                    else
                    {
                        context.Console.WriteMessage( $"Directory '{targetDirectory}' already exists." );
                    }
                }
            }

            return true;
        }

        private string GetVersionPropertiesFilePath( BuildContext context, BuildSettings settings )
        {
            var privateArtifactsRelativeDir =
                this.PrivateArtifactsDirectory.ToString( new BuildInfo( null, settings.BuildConfiguration, this, null ) );

            var artifactsDir = Path.Combine( context.RepoDirectory, privateArtifactsRelativeDir );

            var propsFileName = $"{this.ProductName}.version.props";
            var propsFilePath = Path.Combine( artifactsDir, propsFileName );

            return propsFilePath;
        }

        public void PrepareConfigurationNeutralVersionsFile(
            BuildContext context,
            CommonCommandSettings settings,
            BuildConfiguration buildConfiguration )
        {
            var configurationNeutralVersionsFilePath = this.GetConfigurationNeutralVersionsFilePath( context );
            var configurationSpecificVersionFilePath = context.Product.GetConfigurationSpecificVersionsFilePath( context, settings, buildConfiguration );

            context.Console.WriteMessage( $"Writing '{configurationNeutralVersionsFilePath}'." );

            File.WriteAllText(
                configurationNeutralVersionsFilePath,
                $@"
<!-- File generated by PostSharp.Engineering {VersionHelper.EngineeringVersion}, method {nameof(Product)}.{nameof(this.PrepareConfigurationNeutralVersionsFile)}. -->
<Project>
    <PropertyGroup>
        <EngineeringConfiguration>{buildConfiguration}</EngineeringConfiguration>
    </PropertyGroup>
    <Import Project=""{configurationSpecificVersionFilePath}"" Condition=""'$(DoNotLoadGeneratedVersionFiles)'!='True' AND Exists('{configurationSpecificVersionFilePath}')""/>
</Project>
" );
        }

        public bool PrepareVersionsFile(
            BuildContext context,
            BuildSettings settings,
            [NotNullWhen( true )] out DependenciesOverrideFile? dependenciesOverrideFile )
        {
            var configuration = settings.BuildConfiguration;

            context.Console.WriteMessage( "Preparing the version file" );

            var propsFilePath = this.GetVersionPropertiesFilePath( context, settings );
            Directory.CreateDirectory( Path.GetDirectoryName( propsFilePath )! );

            // Load Versions.g.props.
            if ( !DependenciesOverrideFile.TryLoad( context, settings, configuration, out dependenciesOverrideFile ) )
            {
                return false;
            }

            // If we have any non-feed dependency that does not have a resolved VersionFile, it means that we have not fetched yet. 
            if ( !dependenciesOverrideFile.Fetch( context ) )
            {
                return false;
            }

            // Validate Versions.props. We should not have conditional properties.
            if ( !VersionFile.Validate( context, dependenciesOverrideFile ) )
            {
                return false;
            }

            // We always save the Versions.g.props because it may not exist and it may have been changed by the previous step.
            dependenciesOverrideFile.LocalBuildFile = propsFilePath;

            if ( !dependenciesOverrideFile.TrySave( context, settings ) )
            {
                return false;
            }

            if ( !this.TryReadMainVersionFile( context, out var mainVersionFileInfo, out _ ) )
            {
                return false;
            }

            if ( !this.TryComputeVersion( context, settings, configuration, mainVersionFileInfo, dependenciesOverrideFile, out var version ) )
            {
                return false;
            }

            if ( !GitHelper.TryGetLatestCommitDate( context, out var buildDate ) )
            {
                return false;
            }

            // Generate Versions.g.props.
            if ( !this.TryGenerateManifestFileContent( version, configuration, dependenciesOverrideFile, context, settings, buildDate, out var props ) )
            {
                return false;
            }

            context.Console.WriteMessage( $"Writing '{propsFilePath}'." );
            File.WriteAllText( propsFilePath, props );

            // Generating the configuration-neutral Versions.g.props for the prepared configuration.
            this.PrepareConfigurationNeutralVersionsFile( context, settings, settings.BuildConfiguration );

            return true;
        }

        private bool TryComputeVersion(
            BuildContext context,
            BuildSettings settings,
            BuildConfiguration configuration,
            MainVersionFileInfo mainVersionFileInfo,
            DependenciesOverrideFile dependenciesOverrideFile,
            [NotNullWhen( true )] out VersionComponents? version )
        {
            var configurationLowerCase = configuration.ToString().ToLowerInvariant();

            version = null;
            string? mainVersion = null;

            if ( this.MainVersionDependency != null )
            {
                var mainVersionDependencyName = this.MainVersionDependency.Name;

                // The main version is defined in a dependency. Load the import file.

                if ( !dependenciesOverrideFile.Dependencies.TryGetValue( mainVersionDependencyName, out var dependencySource ) )
                {
                    context.Console.WriteError( $"Cannot find a dependency named '{mainVersionDependencyName}'." );

                    return false;
                }
                else if ( dependencySource.VersionFile == null )
                {
                    context.Console.WriteError( $"The dependency '{mainVersionDependencyName}' is not resolved." );

                    return false;
                }

                var versionFile = Project.FromFile( dependencySource.VersionFile, new ProjectOptions() );

                var propertyName = this.MainVersionDependency!.NameWithoutDot + "MainVersion";

                mainVersion = versionFile.Properties.SingleOrDefault( p => p.Name == propertyName )
                    ?.UnevaluatedValue;

                if ( string.IsNullOrEmpty( mainVersion ) )
                {
                    context.Console.WriteError( $"The file '{dependencySource.VersionFile}' does not contain the {propertyName}." );

                    return false;
                }

                // Note that the version suffix is not copied from the dependency, only the main version. 

                ProjectCollection.GlobalProjectCollection.UnloadAllProjects();
            }

            if ( !string.IsNullOrEmpty( mainVersionFileInfo.OverriddenPatchVersion )
                 && !mainVersionFileInfo.OverriddenPatchVersion.StartsWith( mainVersion ?? mainVersionFileInfo.MainVersion + ".", StringComparison.Ordinal ) )
            {
                context.Console.WriteError(
                    $"The OverriddenPatchVersion property in MainVersion.props ({mainVersionFileInfo.OverriddenPatchVersion}) does not match the MainVersion property value ({mainVersion ?? mainVersionFileInfo.MainVersion})." );

                return false;
            }

            var versionPrefix = mainVersion ?? mainVersionFileInfo.MainVersion;
            string versionSuffix;
            int patchNumber;

            var versionSpec = settings.GetVersionSpec( configuration );
            var versionSpecKind = versionSpec.Kind;

            if ( configuration == BuildConfiguration.Public )
            {
                versionSpecKind = VersionKind.Public;
            }

            switch ( versionSpecKind )
            {
                case VersionKind.Local:
                    {
                        // Local build with timestamp-based version and randomized package number. For the assembly version we use a local incremental file stored in the user profile.

                        var localVersionDirectory = PathHelper.GetEngineeringDataDirectory();

                        var localVersionFile = Path.Combine( localVersionDirectory, $"{this.ProductName}.version" );
                        int localVersion;

                        if ( File.Exists( localVersionFile ) )
                        {
                            localVersion = int.Parse(
                                File.ReadAllText( localVersionFile ),
                                CultureInfo.InvariantCulture ) + 1;
                        }
                        else
                        {
                            localVersion = 1;
                        }

                        if ( localVersion < 1000 )
                        {
                            localVersion = 1000;
                        }

                        if ( !Directory.Exists( localVersionDirectory ) )
                        {
                            Directory.CreateDirectory( localVersionDirectory );
                        }

                        File.WriteAllText( localVersionFile, localVersion.ToString( CultureInfo.InvariantCulture ) );

                        var userName = settings.UserName;
                        versionSuffix = $"local-{userName}-{configurationLowerCase}";

                        patchNumber = localVersion;

                        break;
                    }

                case VersionKind.Numbered:
                    {
                        // Build server build with a build number given by the build server
                        patchNumber = versionSpec.Number;
                        versionSuffix = $"dev-{configurationLowerCase}";

                        break;
                    }

                case VersionKind.Public:
                    // Public build
                    versionSuffix = mainVersionFileInfo.PackageVersionSuffix.TrimStart( '-' );
                    patchNumber = 0;

                    if ( !string.IsNullOrWhiteSpace( mainVersionFileInfo.OverriddenPatchVersion ) )
                    {
                        var parsedOverriddenPatchedVersion = Version.Parse( mainVersionFileInfo.OverriddenPatchVersion );
                        patchNumber = parsedOverriddenPatchedVersion.Revision;
                    }

                    break;

                default:
                    throw new InvalidOperationException();
            }

            version = new VersionComponents( mainVersion ?? mainVersionFileInfo.MainVersion, versionPrefix, patchNumber, versionSuffix );

            return true;
        }

        private bool TryGenerateManifestFileContent(
            VersionComponents version,
            BuildConfiguration configuration,
            DependenciesOverrideFile dependenciesOverrideFile,
            BuildContext context,
            BuildSettings buildSettings,
            string buildDate,
            out string manifestFileContent )
        {
            var success = true;
            
            manifestFileContent = $@"
<!-- File generated by PostSharp.Engineering {VersionHelper.EngineeringVersion}, method {nameof(Product)}.{nameof(this.TryGenerateManifestFileContent)}. -->
<Project>
    <PropertyGroup>
        <{this.ProductNameWithoutDot}MainVersion>{version.MainVersion}</{this.ProductNameWithoutDot}MainVersion>";

            var packageVersionWithoutSuffix = version.PatchNumber == 0 ? version.VersionPrefix : version.VersionPrefix + "." + version.PatchNumber;
            var assemblyVersion = version.VersionPrefix + "." + version.PatchNumber;
            var previewVersionSuffix = configuration == BuildConfiguration.Public ? "preview" : version.VersionSuffix;

            if ( this.GenerateArcadeProperties )
            {
                // Metalama.Compiler, because of Arcade, requires the version number to be decomposed in a prefix, patch number, and suffix.
                // In Arcade, the package naming scheme is different because the patch number is not a part of the package name.

                var arcadeSuffix = string.IsNullOrEmpty( version.VersionSuffix ) ? "" : version.VersionSuffix;
                var previewArcadeSuffix = previewVersionSuffix;

                void AppendToArcadeSuffix( string s )
                {
                    arcadeSuffix += s;
                    previewArcadeSuffix += s;
                }

                if ( version.PatchNumber > 0 )
                {
                    if ( arcadeSuffix.Length > 0 )
                    {
                        AppendToArcadeSuffix( "-" );
                    }
                    else
                    {
                        // It should not happen that we have a patch number without a suffix.
                        AppendToArcadeSuffix( "-patch-" + configuration );
                    }

                    AppendToArcadeSuffix( version.PatchNumber.ToString( CultureInfo.InvariantCulture ) );
                }

                var packageSuffixWithDash = string.IsNullOrEmpty( arcadeSuffix ) ? "" : "-" + arcadeSuffix;
                var packageVersion = version.VersionPrefix + packageSuffixWithDash;
                var packagePreviewVersion = version.VersionPrefix + "-" + previewArcadeSuffix;

                manifestFileContent += $@"
        
        <{this.ProductNameWithoutDot}VersionPrefix>{version.VersionPrefix}</{this.ProductNameWithoutDot}VersionPrefix>
        <{this.ProductNameWithoutDot}VersionSuffix>{arcadeSuffix}</{this.ProductNameWithoutDot}VersionSuffix>
        <{this.ProductNameWithoutDot}VersionPatchNumber>{version.PatchNumber}</{this.ProductNameWithoutDot}VersionPatchNumber>
        <{this.ProductNameWithoutDot}VersionWithoutSuffix>{packageVersionWithoutSuffix}</{this.ProductNameWithoutDot}VersionWithoutSuffix>
        <{this.ProductNameWithoutDot}Version>{packageVersion}</{this.ProductNameWithoutDot}Version>
        <{this.ProductNameWithoutDot}PreviewVersion>{packagePreviewVersion}</{this.ProductNameWithoutDot}PreviewVersion>
        <{this.ProductNameWithoutDot}AssemblyVersion>{assemblyVersion}</{this.ProductNameWithoutDot}AssemblyVersion>";
            }
            else
            {
                var packageSuffix = string.IsNullOrEmpty( version.VersionSuffix ) ? "" : "-" + version.VersionSuffix;
                var packageVersion = packageVersionWithoutSuffix + packageSuffix;
                var packagePreviewVersion = packageVersionWithoutSuffix + "-" + previewVersionSuffix;

                manifestFileContent += $@"
        <{this.ProductNameWithoutDot}Version>{packageVersion}</{this.ProductNameWithoutDot}Version>
        <{this.ProductNameWithoutDot}PreviewVersion>{packagePreviewVersion}</{this.ProductNameWithoutDot}PreviewVersion>
        <{this.ProductNameWithoutDot}AssemblyVersion>{assemblyVersion}</{this.ProductNameWithoutDot}AssemblyVersion>";
            }

            manifestFileContent += $@"
        <{this.ProductNameWithoutDot}BuildConfiguration>{configuration}</{this.ProductNameWithoutDot}BuildConfiguration>
        <{this.ProductNameWithoutDot}Dependencies>{string.Join( ";", this.ParametrizedDependencies.Select( x => x.Name ) )}</{this.ProductNameWithoutDot}Dependencies>
        <{this.ProductNameWithoutDot}PublicArtifactsDirectory>{this.PublicArtifactsDirectory}</{this.ProductNameWithoutDot}PublicArtifactsDirectory>
        <{this.ProductNameWithoutDot}PrivateArtifactsDirectory>{this.PrivateArtifactsDirectory}</{this.ProductNameWithoutDot}PrivateArtifactsDirectory>
        <{this.ProductNameWithoutDot}EngineeringVersion>{VersionHelper.EngineeringVersion}</{this.ProductNameWithoutDot}EngineeringVersion>
        <{this.ProductNameWithoutDot}VersionFilePath>{this.VersionsFilePath}</{this.ProductNameWithoutDot}VersionFilePath>
        <{this.ProductNameWithoutDot}BuildNumber>{buildSettings.BuildNumber}</{this.ProductNameWithoutDot}BuildNumber>
        <{this.ProductNameWithoutDot}BuildType>{buildSettings.BuildType}</{this.ProductNameWithoutDot}BuildType>
        <{this.ProductNameWithoutDot}BuildDate>{buildDate}</{this.ProductNameWithoutDot}BuildDate>
        <{this.ProductNameWithoutDot}ArtifactsDirectory>$(MSBuildThisFileDirectory)</{this.ProductNameWithoutDot}ArtifactsDirectory>
        <RestoreAdditionalProjectSources>$(RestoreAdditionalProjectSources);$(MSBuildThisFileDirectory)</RestoreAdditionalProjectSources>
    </PropertyGroup>
    <ItemGroup>";

            foreach ( var dependency in dependenciesOverrideFile.Dependencies )
            {
                var buildSpec = dependency.Value.BuildServerSource;

                manifestFileContent += $@"
        <{this.ProductNameWithoutDot}Dependencies Include=""{dependency.Key}"">
            <SourceKind>{dependency.Value.SourceKind}</SourceKind>";

                if ( dependency.Value.Version != null )
                {
                    manifestFileContent += $@"
            <Version>{dependency.Value.Version}</Version>";
                }

                switch ( buildSpec )
                {
                    case CiBuildId buildId:
                        manifestFileContent += $@"
            <BuildNumber>{buildId.BuildNumber}</BuildNumber>
            <CiBuildTypeId>{buildId.BuildTypeId}</CiBuildTypeId>";

                        break;

                    case CiLatestBuildOfBranch branch:
                        manifestFileContent += manifestFileContent + $@"
            <Branch>{branch.Name}</Branch>";

                        break;
                }

                manifestFileContent
                    += $@"
        </{this.ProductNameWithoutDot}Dependencies>";
            }

            manifestFileContent += @"
    </ItemGroup>
    <PropertyGroup>
";

            foreach ( var dependency in dependenciesOverrideFile.Dependencies.Where( d => d.Value.SourceKind == DependencySourceKind.Feed ) )
            {
                var nameWithoutDot = dependency.Key.Replace( ".", "", StringComparison.OrdinalIgnoreCase );

                manifestFileContent += $@"
        <{nameWithoutDot}Version Condition=""'$({nameWithoutDot}Version)'==''"">{dependency.Value.Version}</{nameWithoutDot}Version>";
            }

            // Process exported properties.
            foreach ( var kvp in this.ExportedProperties )
            {
                var propsFilePath = Path.Combine( context.RepoDirectory, kvp.Key );
                var propsFile = Project.FromFile( propsFilePath, new ProjectOptions() );

                foreach ( var exportedPropertyName in kvp.Value )
                {
                    var exportedPropertyValue = propsFile
                        .Properties
                        .SingleOrDefault( p => string.Equals( p.Name, exportedPropertyName, StringComparison.OrdinalIgnoreCase ) )
                        ?.EvaluatedValue;

                    if ( string.IsNullOrWhiteSpace( exportedPropertyValue ) )
                    {
                        context.Console.WriteError( $"The exported property '{exportedPropertyName}' in '{propsFilePath}' is not defined." );
                        success = false;
                    }

                    manifestFileContent += $@"
        <{exportedPropertyName} Condition=""'$({exportedPropertyName})'==''"">{exportedPropertyValue}</{exportedPropertyName}>";
                }
            }

            ProjectCollection.GlobalProjectCollection.UnloadAllProjects();

            manifestFileContent += @"
    </PropertyGroup>
</Project>
";

            return success;
        }

        public void Clean( BuildContext context, BuildSettings settings )
        {
            void DeleteDirectory( string directory )
            {
                if ( Directory.Exists( directory ) )
                {
                    context.Console.WriteMessage( $"Deleting directory '{directory}'." );
                    Directory.Delete( directory, true );
                }
            }

            void CleanRecursive( string directory )
            {
                DeleteDirectory( Path.Combine( directory, "bin" ) );
                DeleteDirectory( Path.Combine( directory, "obj" ) );

                foreach ( var subdirectory in Directory.EnumerateDirectories( directory ) )
                {
                    if ( subdirectory == Path.Combine( context.RepoDirectory, this.EngineeringDirectory ) )
                    {
                        // Skip the engineering directory.
                        continue;
                    }

                    CleanRecursive( subdirectory );
                }
            }

            // Clears NuGet global-packages cache of Metalama and PostSharp.Engineering packages to prevent using old or corrupted package.
            void CleanNugetCache()
            {
                // Kill the processes to release the locks on the NuGet cache.
                ProcessKiller.Kill( context.Console );

                // Use dotnet command to locate nuget cache directory.
                ToolInvocationHelper.InvokeTool(
                    context.Console,
                    "dotnet",
                    "nuget locals global-packages -l",
                    context.RepoDirectory,
                    out _,
                    out var output );

                // Get only directory location string.
                var nugetCacheDirectory = output.Split( ' ' )[1].Trim();
                var directoryInfo = new DirectoryInfo( nugetCacheDirectory );

                // Delete all cached packages directories starting with 'Metalama'.
                foreach ( var dir in directoryInfo.EnumerateDirectories( "metalama*" ) )
                {
                    DeleteDirectory( Path.Combine( nugetCacheDirectory, dir.Name ) );
                }

                // Delete all cached packages directories starting with 'PostSharp.Engineering'.
                foreach ( var dir in directoryInfo.EnumerateDirectories( "postsharp.engineering*" ) )
                {
                    DeleteDirectory( Path.Combine( nugetCacheDirectory, dir.Name ) );
                }
            }

            // NugetCache must be automatically deleted only on TeamCity.
            if ( TeamCityHelper.IsTeamCityBuild( settings ) && !DockerHelper.IsDockerBuild() && !settings.NoNuGetCacheCleanup )
            {
                context.Console.WriteHeading( "Cleaning NuGet cache." );
                context.Console.WriteMessage( "The NuGet cache cleanup can be skipped using --no-nuget-cache-cleanup." );

                CleanNugetCache();
            }

            context.Console.WriteHeading( $"Cleaning {this.ProductName}." );

            foreach ( var directory in this.AdditionalDirectoriesToClean )
            {
                DeleteDirectory( Path.Combine( context.RepoDirectory, directory ) );
            }

            var stringParameters = new BuildInfo( null, settings.BuildConfiguration, this, null );

            DeleteDirectory(
                Path.Combine(
                    context.RepoDirectory,
                    this.PrivateArtifactsDirectory.ToString( stringParameters ) ) );

            DeleteDirectory(
                Path.Combine(
                    context.RepoDirectory,
                    this.PublicArtifactsDirectory.ToString( stringParameters ) ) );

            DeleteDirectory(
                Path.Combine(
                    context.RepoDirectory,
                    this.LogsDirectory.ToString() ) );

            foreach ( var directory in Directory.GetDirectories( context.RepoDirectory ) )
            {
                switch ( Path.GetFileName( directory ) )
                {
                    case "source-dependencies":
                    case "dependencies":
                    case ".sonarqube":
                    case { } s when s == this.EngineeringDirectory:
                        continue;

                    default:
                        CleanRecursive( directory );

                        break;
                }
            }
        }

        private (string Private, string Public) GetArtifactsDirectories( BuildContext context, BuildInfo buildInfo )
        {
            return (
                Path.Combine( context.RepoDirectory, this.PrivateArtifactsDirectory.ToString( buildInfo ) ),
                Path.Combine( context.RepoDirectory, this.PublicArtifactsDirectory.ToString( buildInfo ) )
            );
        }

        public bool Verify( BuildContext context, PublishSettings settings )
        {
            var configuration = settings.BuildConfiguration;

            if ( configuration == BuildConfiguration.Public )
            {
                var versionFile = this.ReadGeneratedVersionFile( context.GetManifestFilePath( configuration ) );
                var directories = this.GetArtifactsDirectories( context, versionFile );

                // Verify that public packages have no private dependencies.
                if ( !VerifyPublicPackageCommand.Execute(
                        context.Console,
                        new VerifyPublicPackageCommandSettings { Directory = directories.Public } ) )
                {
                    return false;
                }

                return true;
            }
            else
            {
                context.Console.WriteError( "Artifacts can only be verified for the public build." );

                return false;
            }
        }

        private bool CanPublish( BuildContext context, PublishSettings settings )
        {
            if ( !this.TryReadMainVersionFile( context, out var mainVersionFileInfo, out _ ) )
            {
                return false;
            }

            if ( !this.TryGetPreparedVersionInfo(
                    context,
                    mainVersionFileInfo,
                    out var preparedVersionInfo ) )
            {
                return false;
            }

            // Only versioned products require version bump.
            if ( this.DependencyDefinition.IsVersioned )
            {
                // Analyze the repository state since the last deployment.
                if ( !TryAnalyzeGitHistory(
                        context,
                        mainVersionFileInfo,
                        out var hasBumpSinceLastDeployment,
                        out var hasChangesSinceLastDeployment,
                        out var lastVersionTag ) )
                {
                    return false;
                }

                // If there are no changes since the deployment, we get only a warning and deployment proceeds with the same version.
                if ( !hasChangesSinceLastDeployment )
                {
                    context.Console.WriteWarning( $"There are no new unpublished changes since the last deployment." );
                }
                else
                {
                    // To check if version was bumped manually we get full prepared version info.
                    var currentVersion = preparedVersionInfo.Version + preparedVersionInfo.PackageVersionSuffix;

                    // Publishing fails if there are changes and the version has not been bumped since the last deployment.
                    if ( !hasBumpSinceLastDeployment && currentVersion == lastVersionTag )
                    {
                        context.Console.WriteError( "There are changes since the last deployment but the version has not been bumped." );

                        return false;
                    }
                }
            }

            return true;
        }

        public bool PrePublish( BuildContext context, PublishSettings settings )
        {
            // This step is only required for pre-publishing and post-publishing, so they don't require a build.
            // Publishing gets this file along with the published artifacts.
            if ( !this.PrepareVersionsFile( context, settings, out _ ) )
            {
                return false;
            }

            if ( this.ProductFamily.UpstreamProductFamily != null && !DownstreamMerge.CheckUpstreamChanges( context, settings ) )
            {
                return false;
            }

            // Check that we're ready to publish.
            if ( !this.CanPublish( context, settings ) )
            {
                return false;
            }
            
            if ( TeamCityHelper.IsTeamCityBuild( settings ) )
            {
                // When on TeamCity, Git user credentials are set to TeamCity.
                if ( !TeamCityHelper.TrySetGitIdentityCredentials( context ) )
                {
                    return false;
                }
            }

            var sourceBranch = context.Product.DependencyDefinition.Branch;

            if ( context.Branch != sourceBranch )
            {
                context.Console.WriteError(
                    $"Pre-publishing can only be executed on the development branch ('{sourceBranch}'). The current branch is '{context.Branch}'." );

                return false;
            }

            var targetBranch = context.Product.DependencyDefinition.ReleaseBranch;

            if ( targetBranch == null )
            {
                context.Console.WriteError( $"Pre-publishing failed. The release branch is not set for '{context.Product.ProductName}' product." );

                return false;
            }
            
            if ( !GitHelper.TryPullAndMergeAndPush( context, settings, targetBranch ) )
            {
                return false;
            }
            
            // Act as a local dependency for subsequent projects, that use the --use-local-dependencies flag.
            this.WriteImportFile( context, settings.BuildConfiguration );

            return true;
        }

        public bool Publish( BuildContext context, PublishSettings settings )
        {
            context.Console.WriteHeading( "Publishing files" );

            if ( !context.Product.IsPublishingNonReleaseBranchesAllowed && !settings.IsStandalone )
            {
                var releaseBranch = context.Product.DependencyDefinition.ReleaseBranch;

                // If the release branch is not specified, the pre and post publishing is not required, and the publishing can be performed from any branch. 
                if ( releaseBranch != null && context.Branch != releaseBranch )
                {
                    context.Console.WriteError(
                        $"Publishing can only be executed on the release branch ('{releaseBranch}'). The current branch is '{context.Branch}'." );

                    return false;
                }
            }

            if ( !this.CanPublish( context, settings ) )
            {
                return false;
            }
            
            // TODO: Verification is broken - NuGet verification is slow and makes the verification fail
            // on seemimngly unpublished packages.
            // if ( settings.BuildConfiguration == BuildConfiguration.Public )
            // {
            //     if ( !this.Verify( context, settings ) )
            //     {
            //         return false;
            //     }
            // }

            var configuration = settings.BuildConfiguration;
            var buildInfo = this.ReadGeneratedVersionFile( context.GetManifestFilePath( configuration ) );
            var directories = this.GetArtifactsDirectories( context, buildInfo );
            var configurationInfo = this.Configurations.GetValue( configuration );
            var hasTarget = false;
            
            if ( !Publisher.PublishDirectory(
                    context,
                    settings,
                    directories,
                    configurationInfo,
                    buildInfo,
                    false,
                    ref hasTarget ) )
            {
                return false;
            }

            if ( !Publisher.PublishDirectory(
                    context,
                    settings,
                    directories,
                    configurationInfo,
                    buildInfo,
                    true,
                    ref hasTarget ) )
            {
                return false;
            }

            // For consolidated deployments, this is part of the post-deployment step.
            if ( !this.ProductFamily.HasConsolidatedBuild || settings.IsStandalone )
            {
                if ( !TryUpdateAutoUpdatedDependencies( context, settings ) )
                {
                    context.Console.WriteError( "Failed to update auto-updated dependencies." );

                    return false;
                }

                if ( !this.TryAddTagToLastCommit( context, settings ) )
                {
                    context.Console.WriteError( "Failed to tag the latest commit." );

                    return false;
                }

                var releaseBranch = context.Product.DependencyDefinition.ReleaseBranch;

                if ( releaseBranch != null && context.Branch == context.Product.DependencyDefinition.Branch )
                {
                    if ( !GitHelper.TryPullAndMergeAndPush( context, settings, releaseBranch ) )
                    {
                        return false;
                    }
                }
            }

            if ( !hasTarget )
            {
                context.Console.WriteWarning( "No active publishing target was detected." );
            }
            else
            {
                context.Console.WriteSuccess( "Publishing has succeeded." );
            }

            // Swap after successful publishing.
            if ( configurationInfo.SwapAfterPublishing )
            {
                context.Console.WriteMessage( "Swapping staging and production slots after publishing." );

                if ( !this.SwapAfterPublishing( context, settings ) )
                {
                    context.Console.WriteError( "Failed to swap after publishing." );

                    return false;
                }

                context.Console.WriteSuccess( "Swap after publishing has succeeded." );
            }

            return true;
        }

        public bool PostPublish( BuildContext context, PublishSettings settings )
        {
            context.Console.WriteHeading( "Finishing publishig." );
            
            // This step is only required for pre-publishing and post-publishing, so they don't require a build.
            // Publishing gets this file along with the published artifacts.
            if ( !this.PrepareVersionsFile( context, settings, out _ ) )
            {
                return false;
            }
            
            if ( TeamCityHelper.IsTeamCityBuild( settings ) )
            {
                // When on TeamCity, Git user credentials are set to TeamCity.
                if ( !TeamCityHelper.TrySetGitIdentityCredentials( context ) )
                {
                    return false;
                }
            }
            
            var sourceBranch = context.Product.DependencyDefinition.ReleaseBranch;

            if ( sourceBranch == null )
            {
                context.Console.WriteError( $"Post-publishing failed. The release branch is not set for '{context.Product.ProductName}' product." );

                return false;
            }
            
            if ( context.Branch != sourceBranch )
            {
                context.Console.WriteError(
                    $"Post-publishing can only be executed on the release branch ('{sourceBranch}'). The current branch is '{context.Branch}'." );

                return false;
            }
            
            if ( !TryUpdateAutoUpdatedDependencies( context, settings ) )
            {
                context.Console.WriteError( "Failed to update auto-updated dependencies." );
                
                return false;
            }

            if ( !this.TryAddTagToLastCommit( context, settings ) )
            {
                context.Console.WriteError( "Failed to tag the latest commit." );
                
                return false;
            }
            
            // Merge the release branch back to develop branch.
            if ( !GitHelper.TryPullAndMergeAndPush( context, settings, context.Product.DependencyDefinition.Branch ) )
            {
                return false;
            }

            // Act as a local dependency for subsequent projects, that use the --use-local-dependencies flag.
            this.WriteImportFile( context, settings.BuildConfiguration );
            
            context.Console.WriteSuccess( "Publishing finished successfuly." );

            return true;
        }

        private static bool TryUpdateAutoUpdatedDependencies( BuildContext context, PublishSettings settings )
        {
            // Go through all dependencies and update their fixed version in AutoUpdatedVersions.props file.
            if ( !AutoUpdatedDependenciesHelper.TryParseAndVerifyDependencies( context, settings, out var dependenciesUpdated ) )
            {
                return false;
            }

            // Commit and push if dependencies versions were updated in previous step.
            if ( dependenciesUpdated )
            {
                // Adds AutoUpdatedVersions.props with updated dependencies versions to Git staging area.
                if ( !ToolInvocationHelper.InvokeTool(
                        context.Console,
                        "git",
                        $"add {context.Product.AutoUpdatedVersionsFilePath}",
                        context.RepoDirectory ) )
                {
                    return false;
                }

                // Returns the remote origin.
                if ( !ToolInvocationHelper.InvokeTool(
                        context.Console,
                        "git",
                        "remote get-url origin",
                        context.RepoDirectory,
                        out _,
                        out var gitOrigin ) )
                {
                    return false;
                }

                if ( !ToolInvocationHelper.InvokeTool(
                        context.Console,
                        "git",
                        "commit -m \"<<DEPENDENCIES_UPDATED>>\"",
                        context.RepoDirectory ) )
                {
                    return false;
                }

                if ( !ToolInvocationHelper.InvokeTool(
                        context.Console,
                        "git",
                        $"push {gitOrigin.Trim()}",
                        context.RepoDirectory ) )
                {
                    return false;
                }
            }

            return true;
        }

        public bool SwapAfterPublishing( BuildContext context, PublishSettings publishSettings )
        {
            var swapSettings = new SwapSettings() { BuildConfiguration = publishSettings.BuildConfiguration, Dry = publishSettings.Dry };

            return this.Swap( context, swapSettings );
        }

        public bool Swap( BuildContext context, SwapSettings settings )
        {
            var configuration = this.Configurations.GetValue( settings.BuildConfiguration );
            var buildInfo = this.ReadGeneratedVersionFile( context.GetManifestFilePath( settings.BuildConfiguration ) );
            var directories = this.GetArtifactsDirectories( context, buildInfo );

            var success = true;

            if ( configuration.Swappers != null )
            {
                foreach ( var swapper in configuration.Swappers )
                {
                    switch ( swapper.Execute( context, settings, configuration, buildInfo ) )
                    {
                        case SuccessCode.Success:
                            foreach ( var tester in swapper.Testers )
                            {
                                switch ( tester.Execute( context, directories.Private, buildInfo, settings.Dry ) )
                                {
                                    case SuccessCode.Success:
                                        break;

                                    // If any of the testers fail during swap, we do swap again to get the slots to their original state.
                                    case SuccessCode.Error:
                                        context.Console.WriteError(
                                            $"Tester failed after swapping staging and production slots. Attempting to revert the swap." );

                                        switch ( swapper.Execute( context, settings, configuration, buildInfo ) )
                                        {
                                            case SuccessCode.Success:
                                                context.Console.WriteMessage( "Successfully reverted swap." );

                                                break;

                                            case SuccessCode.Error:
                                                context.Console.WriteError( "Failed to revert swap." );

                                                break;

                                            case SuccessCode.Fatal:
                                                return false;
                                        }

                                        success = false;

                                        break;

                                    case SuccessCode.Fatal:
                                        return false;

                                    default:
                                        throw new NotImplementedException();
                                }
                            }

                            break;

                        case SuccessCode.Error:
                            success = false;

                            break;

                        case SuccessCode.Fatal:
                            return false;

                        default:
                            throw new NotImplementedException();
                    }
                }
            }

            return success;
        }

        public bool BumpVersion( BuildContext context, BumpSettings settings )
        {
            context.Console.WriteHeading( $"Bumping the '{context.Product.ProductName}' version." );

            var developmentBranch = context.Product.DependencyDefinition.Branch;

            if ( context.Branch != developmentBranch )
            {
                context.Console.WriteError(
                    $"The version bump can only be executed on the development branch ('{developmentBranch}'). The current branch is '{context.Branch}'." );

                return false;
            }

            // It is forbidden to push to the release branch, but it occasionally happens.
            // We need to make sure that there are no pending changes in the release branch to be merged to the development branch.
            // Failing to do so could result in missing published changes, and it could also break the version bump.
            var releaseBranch = context.Product.DependencyDefinition.ReleaseBranch;

            if ( releaseBranch == null )
            {
                context.Console.WriteMessage(
                    "Skipping check for pending changes from the release branch, as the release branch is not set for this product." );
            }
            else
            {
                context.Console.WriteMessage( $"Checking for pending changes from the release branch ('{releaseBranch}')." );

                if ( !GitHelper.TryCheckoutAndPull( context, releaseBranch ) )
                {
                    return false;
                }

                if ( !GitHelper.TryCheckoutAndPull( context, context.Branch ) )
                {
                    return false;
                }

                if ( !GitHelper.TryGetCommitsCount( context, "HEAD", releaseBranch, out var count ) )
                {
                    return false;
                }

                if ( count > 0 )
                {
                    context.Console.WriteError( $"There are pending changes from the '{releaseBranch}' branch." );
                    context.Console.WriteError( $"Check the relevancy of the changes and merge the '{releaseBranch}' branch to the '{developmentBranch}'." );
                    context.Console.WriteError( "Failing to do so could result in invalid version number of this product." );

                    return false;
                }
            }

            if ( !this.TryReadMainVersionFile( context, out var currentMainVersionFile ) )
            {
                return false;
            }

            // If the version has already been dumped since the last deployment, there is nothing to do. 
            if ( !TryAnalyzeGitHistory( context, currentMainVersionFile, out var hasBumpSinceLastDeployment, out var hasChangesSinceLastDeployment, out _ ) )
            {
                return false;
            }

            if ( hasBumpSinceLastDeployment && !settings.OverridePreviousBump )
            {
                context.Console.WriteWarning( "Version has already been bumped since the last deployment." );

                return true;
            }

            // Read the current version of the dependencies directly from source control.
            if ( !this.TryReadDependencyVersionsFromSourceRepos( context, true, out var dependencyVersions ) )
            {
                return false;
            }

            // Comparing the actual version of dependencies with the versions stored during the last bump.
            var newBumpInfoFile =
                new BumpInfoFile( dependencyVersions );

            var bumpInfoFilePath = Path.Combine(
                context.RepoDirectory,
                this.BumpInfoFilePath );

            var oldBumpFileContent = File.Exists( bumpInfoFilePath ) ? File.ReadAllText( bumpInfoFilePath ) : "";
            var hasChangesInDependencies = newBumpInfoFile.ToString() != oldBumpFileContent;

            if ( !hasChangesInDependencies && !hasChangesSinceLastDeployment )
            {
                context.Console.WriteWarning( $"There are no changes since the last deployment." );

                return true;
            }

            // If there is a change in dependencies versions, we update BumpInfo.txt with changes.
            if ( hasChangesInDependencies )
            {
                context.Console.WriteMessage(
                    $"'{bumpInfoFilePath}' contents are outdated. Overwriting its old content '{oldBumpFileContent}' with new content '{newBumpInfoFile}'." );

                File.WriteAllText( bumpInfoFilePath, newBumpInfoFile.ToString() );
            }

            Version? oldVersion;
            Version? newVersion;

            if ( this.MainVersionDependency == null )
            {
                if ( !this.BumpStrategy.TryBumpVersion( this, context, out oldVersion, out newVersion ) )
                {
                    return false;
                }
            }
            else
            {
                if ( hasChangesSinceLastDeployment && !hasChangesInDependencies )
                {
                    const string message =
                        "There are changes in the current repo but no changes in dependencies. However, the current repo does not have its own versioning.";

                    if ( settings.Force )
                    {
                        context.Console.WriteImportantMessage( $"{message} This is being ignored using --force." );

                        return true;
                    }

                    context.Console.WriteError( $"{message} Do a fake change in a parent repo or use --force." );

                    return false;
                }

                var oldBumpInfo = BumpInfoFile.FromText( oldBumpFileContent );
                newVersion = dependencyVersions[this.MainVersionDependency.Name];
                oldVersion = oldBumpInfo?.Dependencies[this.MainVersionDependency.Name];
            }

            // Commit the version bump.
            if ( !this.TryCommitVersionBump( context, oldVersion, newVersion, settings ) )
            {
                return false;
            }

            return true;
        }

        private bool TryReadDependencyVersionsFromSourceRepos(
            BuildContext context,
            bool snapshotDependenciesOnly,
            [NotNullWhen( true )] out Dictionary<string, Version>? dependencyVersions )
        {
            dependencyVersions = new Dictionary<string, Version>();

            var allDependencies =
                this.ParametrizedDependencies.Select( x => x.Definition )
                    .Union( this.SourceDependencies )
                    .Union( this.MainVersionDependency == null ? Enumerable.Empty<DependencyDefinition>() : new[] { this.MainVersionDependency } );

            foreach ( var dependency in allDependencies )
            {
                if ( snapshotDependenciesOnly && !dependency.GenerateSnapshotDependency )
                {
                    continue;
                }

                var mainVersionFile = $"{dependency.EngineeringDirectory}/MainVersion.props";
                context.Console.WriteMessage( $"Downloading '{mainVersionFile}' from '{dependency.VcsRepository}'." );

                if ( !dependency.VcsRepository.TryDownloadTextFile( context.Console, dependency.Branch, mainVersionFile, out var mainVersionContent ) )
                {
                    return false;
                }

                var document = XDocument.Parse( mainVersionContent );
                var project = Project.FromXmlReader( document.CreateReader(), new ProjectOptions() );
                var mainVersionPropertyValue = project.Properties.FirstOrDefault( p => p.Name == "MainVersion" )?.EvaluatedValue;

                if ( string.IsNullOrEmpty( mainVersionPropertyValue ) )
                {
                    context.Console.WriteError(
                        $"The property 'MainVersion' or its value in '{mainVersionFile}' of dependency '{dependency.Name}' is not defined." );

                    return false;
                }

                dependencyVersions.Add( dependency.Name, Version.Parse( mainVersionPropertyValue ) );
            }

            return true;
        }

        internal bool GenerateTeamcityConfiguration( BuildContext context, CommonCommandSettings settings )
        {
            context.Console.WriteHeading( "Generating build integration scripts" );

            var configurations = new[] { BuildConfiguration.Debug, BuildConfiguration.Release, BuildConfiguration.Public };
            var teamCityBuildConfigurations = new List<TeamCityBuildConfiguration>();
            var isRepoRemoteSsh = this.DependencyDefinition.VcsRepository.IsSshAgentRequired;
            var defaultBranch = this.DependencyDefinition.Branch;

            var deploymentBranch = this.ProductFamily.HasConsolidatedBuild
                ? this.DependencyDefinition.ReleaseBranch ?? defaultBranch
                : defaultBranch;

            var defaultBranchParameter = this.DependencyDefinition.VcsRepository.DefaultBranchParameter;
            var vcsRootId = TeamCityHelper.GetVcsRootId( this.DependencyDefinition );

            foreach ( var configuration in configurations )
            {
                var configurationInfo = this.Configurations[configuration];

                if ( !configurationInfo.ExportsToTeamCityBuild )
                {
                    continue;
                }

                var versionInfo = new BuildInfo( null, configuration, this, null );

                // Set artifact rules.
                var publicArtifactsDirectory =
                    context.Product.PublicArtifactsDirectory.ToString( versionInfo ).Replace( "\\", "/", StringComparison.Ordinal );

                var privateArtifactsDirectory =
                    context.Product.PrivateArtifactsDirectory.ToString( versionInfo ).Replace( "\\", "/", StringComparison.Ordinal );

                var testResultsDirectory =
                    context.Product.TestResultsDirectory.ToString( versionInfo ).Replace( "\\", "/", StringComparison.Ordinal );

                var logsDirectory = context.Product.LogsDirectory.ToString( versionInfo ).Replace( "\\", "/", StringComparison.Ordinal );

                var deployedArtifactRules = $"+:{publicArtifactsDirectory}/**/*=>{publicArtifactsDirectory}";
                deployedArtifactRules += $@"\n+:{privateArtifactsDirectory}/**/*=>{privateArtifactsDirectory}";

                var publishedArtifactRules = deployedArtifactRules;
                publishedArtifactRules += $@"\n+:{testResultsDirectory}/**/*=>{testResultsDirectory}";
                publishedArtifactRules += $@"\n+:{logsDirectory}/**/*=>logs";

                var additionalArtifactRules = this.DefaultArtifactRules;

                if ( configurationInfo.AdditionalArtifactRules != null )
                {
                    additionalArtifactRules = this.DefaultArtifactRules.AddRange( configurationInfo.AdditionalArtifactRules );
                }

                if ( !DependenciesOverrideFile.TryLoad( context, settings, configuration, out var dependenciesOverrideFile ) )
                {
                    return false;
                }

                if ( !dependenciesOverrideFile.Fetch( context ) )
                {
                    return false;
                }

                var dependencies =
                    dependenciesOverrideFile.Dependencies.Select(
                            x => (Name: x.Key,
                                  Definition: this.ProductFamily.GetDependencyDefinition( x.Key ),
                                  Source: x.Value) )
                        .Where( d => d.Definition.GenerateSnapshotDependency )
                        .Select( x => (x.Name, x.Definition, Configuration: GetDependencyConfiguration( x.Definition, x.Source )) )
                        .ToList();

                var snapshotDependencies = dependencies
                    .Select(
                        d => new TeamCitySnapshotDependency(
                            d.Definition.CiConfiguration.BuildTypes[d.Configuration],
                            true,
                            $"+:{d.Definition.PrivateArtifactsDirectory.ToString( new BuildInfo( null, d.Configuration, this, null ) ).Replace( Path.DirectorySeparatorChar, '/' )}/**/*=>dependencies/{d.Name}" ) )
                    .ToList();

                var sourceSnapshotDependencies = this.SourceDependencies.Where( d => d.GenerateSnapshotDependency )
                    .Select( d => new TeamCitySnapshotDependency( d.CiConfiguration.BuildTypes[configuration], true ) );

                var buildDependencies = snapshotDependencies.Concat( sourceSnapshotDependencies ).OrderBy( d => d.ObjectId ).ToArray();

                var sourceDependencies = this.SourceDependencies.Select(
                        d => new TeamCitySourceDependency(
                            d.CiConfiguration.ProjectId.ToString(),
                            true,
                            $"+:. => {this.SourceDependenciesDirectory}/{d.Name}" ) )
                    .ToArray();

                var teamCityBuildSteps = new List<TeamCityBuildStep>();

                if ( !this.UseDockerInTeamcity )
                {
                    teamCityBuildSteps.Add( new TeamCityEngineeringCommandBuildStep( "PreKill", "Kill background processes before cleanup", "tools kill" ) );
                }

                var requiresUpstreamCheck =

                    // The check is required.
                    configurationInfo.RequiresUpstreamCheck

                    // There is upstream product to check.
                    && this.ProductFamily.UpstreamProductFamily != null

                    // For products with the release branch, the check is done as part of the deployment preparation step.
                    && this.DependencyDefinition.ReleaseBranch == null;

                if ( requiresUpstreamCheck )
                {
                    teamCityBuildSteps.Add(
                        new TeamCityEngineeringCommandBuildStep(
                            "UpstreamCheck",
                            "Check pending upstream changes",
                            "tools git check-upstream",
                            areCustomArgumentsAllowed: true ) );
                }

                teamCityBuildSteps.Add( new TeamCityEngineeringBuildBuildStep( configuration, true, this.UseDockerInTeamcity ) );
                
                if ( !this.UseDockerInTeamcity )
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
                    this.ResolvedBuildAgentRequirements )
                {
                    BuildSteps = teamCityBuildSteps.ToArray(),
                    ArtifactRules = publishedArtifactRules,
                    AdditionalArtifactRules = additionalArtifactRules.ToArray(),
                    BuildTriggers = configurationInfo.BuildTriggers,
                    SnapshotDependencies = buildDependencies,
                    SourceDependencies = sourceDependencies,
                    BuildTimeOutThreshold = configurationInfo.BuildTimeOutThreshold ?? this.BuildTimeOutThreshold,
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
                            true );

                    if ( configurationInfo.ExportsToTeamCityDeploy )
                    {
                        teamCityDeploymentConfiguration = new TeamCityBuildConfiguration(
                            $"{configuration}Deployment",
                            configurationInfo.TeamCityDeploymentName ?? $"Deploy [{configuration}]",
                            deploymentBranch,
                            defaultBranchParameter,
                            vcsRootId,
                            this.ResolvedBuildAgentRequirements )
                        {
                            BuildSteps = [CreatePublishBuildStep()],
                            IsDeployment = true,
                            SnapshotDependencies = buildDependencies.Where( d => d.ArtifactRules != null )
                                .Concat( new[] { new TeamCitySnapshotDependency( teamCityBuildConfiguration.ObjectName, false, deployedArtifactRules ) } )
                                .Concat(
                                    this.ParametrizedDependencies.Select( d => d.Definition )
                                        .Union( this.SourceDependencies )
                                        .Where( d => d is { GenerateSnapshotDependency: true, CiConfiguration.DeploymentBuildType: not null } )
                                        .Select( d => new TeamCitySnapshotDependency( d.CiConfiguration.DeploymentBuildType!, true ) ) )
                                .OrderBy( d => d.ObjectId )
                                .ToArray(),
                            BuildTimeOutThreshold = configurationInfo.DeploymentTimeOutThreshold ?? this.DeploymentTimeOutThreshold,
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
                            buildAgentRequirements: this.ResolvedBuildAgentRequirements )
                        {
                            BuildSteps = [CreatePublishBuildStep( true )],
                            IsDeployment = true,
                            SnapshotDependencies = buildDependencies.Where( d => d.ArtifactRules != null )
                                .Concat( new[] { new TeamCitySnapshotDependency( teamCityBuildConfiguration.ObjectName, false, deployedArtifactRules ) } )
                                .OrderBy( d => d.ObjectId )
                                .ToArray(),
                            BuildTimeOutThreshold = configurationInfo.DeploymentTimeOutThreshold ?? this.DeploymentTimeOutThreshold,
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
                            buildAgentRequirements: this.ResolvedBuildAgentRequirements )
                        {
                            BuildSteps =
                            [
                                new TeamCityEngineeringCommandBuildStep( "Swap", "Swap", "swap", $"--configuration {configuration}", true )
                            ],
                            IsDeployment = true,
                            SnapshotDependencies = swapDependencies.OrderBy( d => d.ObjectId ).ToArray(),
                            BuildTimeOutThreshold = configurationInfo.SwapTimeOutThreshold ?? this.SwapTimeOutThreshold
                        } );
                }
            }

            // Only versioned products that don't have consolidated version bump can be bumped individually.
            if ( !this.ProductFamily.HasConsolidatedBuild && this.DependencyDefinition.IsVersioned )
            {
                var dependencies = this.ParametrizedDependencies;

                if ( dependencies != null! )
                {
                    teamCityBuildConfigurations.Add(
                        new TeamCityBuildConfiguration(
                            objectName: "VersionBump",
                            name: $"Version Bump",
                            defaultBranch,
                            defaultBranchParameter,
                            vcsRootId,
                            buildAgentRequirements: this.ResolvedBuildAgentRequirements )
                        {
                            BuildSteps =
                                [new TeamCityEngineeringCommandBuildStep( "Bump", "Bump", "bump", areCustomArgumentsAllowed: true )],
                            BuildTimeOutThreshold = this.VersionBumpTimeOutThreshold,
                            IsSshAgentRequired = isRepoRemoteSsh
                        } );
                }
            }

            // Create a TeamCity configuration for downstream merge.
            if ( this.ProductFamily.DownstreamProductFamily != null )
            {
                var snapshotDependencies = this.Configurations[BuildConfiguration.Debug].ExportsToTeamCityBuild
                    ? new[] { new TeamCitySnapshotDependency( "DebugBuild", false ) }
                    : null;

                teamCityBuildConfigurations.Add(
                    new TeamCityBuildConfiguration(
                        "DownstreamMerge",
                        "Downstream Merge",
                        defaultBranch,
                        defaultBranchParameter,
                        vcsRootId,
                        this.ResolvedBuildAgentRequirements )
                    {
                        BuildSteps =
                        [
                            new TeamCityEngineeringCommandBuildStep(
                                "DownstreamMerge",
                                "Merge downstream",
                                "tools git merge-downstream",
                                areCustomArgumentsAllowed: true )
                        ],
                        SnapshotDependencies = snapshotDependencies,
                        BuildTriggers = [new SourceBuildTrigger()],
                        BuildTimeOutThreshold = this.DownstreamMergeTimeOutThreshold,
                        IsSshAgentRequired = isRepoRemoteSsh
                    } );
            }

            // Add from extensions.
            foreach ( var extension in this.Extensions )
            {
                if ( !extension.AddTeamcityBuildConfiguration( context, teamCityBuildConfigurations ) )
                {
                    return false;
                }
            }
            
            var teamCityProject = new TeamCityProject( teamCityBuildConfigurations.ToArray() );

            TeamCityHelper.GeneratePom( context, this.DependencyDefinition.CiConfiguration.ProjectId.Id, this.DependencyDefinition.CiConfiguration.BaseUrl );
            TeamCityHelper.GenerateTeamCityConfiguration( context, teamCityProject );

            return true;
        }

        private static BuildConfiguration GetDependencyConfiguration( DependencyDefinition definition, DependencySource source )
        {
            if ( source.SourceKind == DependencySourceKind.Feed )
            {
                throw new InvalidOperationException( "The TeamCity file cannot be generated when a dependency source is Feed." );
            }

            if ( source.VersionFile == null )
            {
                throw new InvalidOperationException( $"The dependency '{definition.Name}' is not resolved. " );
            }

            var project = Project.FromFile( source.VersionFile, new ProjectOptions() );
            var property = project.AllEvaluatedProperties.Single( p => p.Name == $"{definition.NameWithoutDot}BuildConfiguration" );

            ProjectCollection.GlobalProjectCollection.UnloadAllProjects();

            return Enum.Parse<BuildConfiguration>( property.EvaluatedValue );
        }

        private static bool TryAnalyzeGitHistory(
            BuildContext context,
            MainVersionFileInfo mainVersionFileInfo,
            out bool hasBumpSinceLastDeployment,
            out bool hasChangesSinceLastDeployment,
            [NotNullWhen( true )] out string? lastTagVersion )
        {
            lastTagVersion = null;

            // Fetch remote for tags and commits to make sure we have the full history to compare tags against.
            if ( !GitHelper.TryFetch( context, null ) )
            {
                hasBumpSinceLastDeployment = false;
                hasChangesSinceLastDeployment = false;

                return false;
            }

            // Get string of the last published release tag matched by glob pattern and trim newline.
            var globMatch = $"release/{mainVersionFileInfo.Release}.*";

            ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"describe --abbrev=0 --tags --match \"{globMatch}\"",
                context.RepoDirectory,
                out var exitCode,
                out var gitTagOutput );

            if ( exitCode != 0 )
            {
                hasBumpSinceLastDeployment = false;
                hasChangesSinceLastDeployment = false;

                context.Console.WriteError( gitTagOutput );

                context.Console.WriteError(
                    $"The repository may not have any tags matching pattern: '{globMatch}'. If so add 'release/{mainVersionFileInfo.Release}.0{mainVersionFileInfo.PackageVersionSuffix}' tag to initial commit." );

                return false;
            }

            var lastTag = gitTagOutput.Trim();
            lastTagVersion = lastTag.Replace( "release/", "", StringComparison.OrdinalIgnoreCase );

            // Get commits log since the last deployment formatted to one line per commit.
            // Note that the log does NOT include the released commit.
            // ReSharper disable once StringLiteralTypo
            ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"log \"{lastTag}..HEAD\" --oneline",
                context.RepoDirectory,
                out exitCode,
                out var gitLogOutput );

            if ( exitCode != 0 )
            {
                hasBumpSinceLastDeployment = false;
                hasChangesSinceLastDeployment = false;

                context.Console.WriteError( gitLogOutput );

                return false;
            }

            // Check if we bumped since last deployment by looking in the Git log. 
            var gitLog = gitLogOutput.Split( new[] { '\n', '\r' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );

            var versionBumpLogCommentRegex =
                new Regex( GitHelper.GetEngineeringCommitsRegex( true, false, context.Product.DependencyDefinition.ProductFamily ) );

            var lastVersionDump = gitLog.Select( ( s, i ) => (Log: s, LineNumber: i) )
                .FirstOrDefault( s => versionBumpLogCommentRegex.IsMatch( s.Log.Split( ' ', 2, StringSplitOptions.TrimEntries )[1] ) );

            hasBumpSinceLastDeployment = lastVersionDump.Log != null;

            // Get count of commits since last deployment excluding version bumps and check if there are any changes.
            if ( !GitHelper.TryGetCommitsCount( context, lastTag, "HEAD", context.Product.DependencyDefinition.ProductFamily, out var commitsSinceLastTag ) )
            {
                hasBumpSinceLastDeployment = false;
                hasChangesSinceLastDeployment = false;

                return false;
            }

            hasChangesSinceLastDeployment = commitsSinceLastTag > 0;

            return true;
        }

        private bool TryAddTagToLastCommit( BuildContext context, BaseBuildSettings settings )
        {
            if ( !this.TryReadMainVersionFile( context, out var mainVersionFileInfo ) )
            {
                return false;
            }

            if ( !this.TryGetPreparedVersionInfo(
                    context,
                    mainVersionFileInfo,
                    out var preparedVersionInfo ) )
            {
                return false;
            }
            
            var versionTag = string.Concat( "release/", preparedVersionInfo.Version, preparedVersionInfo.PackageVersionSuffix );

            ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"ls-remote --tags origin --grep {versionTag}",
                context.RepoDirectory,
                out _,
                out var gitOutput );

            if ( gitOutput.Contains( versionTag, StringComparison.OrdinalIgnoreCase ) )
            {
                context.Console.WriteWarning( $"Repository already contains tag '{versionTag}'." );

                return true;
            }

            // Tagging the last commit with version.
            if ( !ToolInvocationHelper.InvokeTool(
                    context.Console,
                    "git",
                    $"tag \"{versionTag}\"",
                    context.RepoDirectory ) )
            {
                return false;
            }

            // Returns the remote origin.
            if ( !ToolInvocationHelper.InvokeTool(
                    context.Console,
                    "git",
                    $"remote get-url origin",
                    context.RepoDirectory,
                    out _,
                    out var gitOrigin ) )
            {
                return false;
            }

            gitOrigin = gitOrigin.Trim();
            var isHttps = gitOrigin.StartsWith( "https", StringComparison.InvariantCulture );

            // When on TeamCity, if the repository is of HTTPS origin, the origin will be updated to form including Git authentication credentials.
            if ( TeamCityHelper.IsTeamCityBuild( settings ) )
            {
                if ( isHttps )
                {
                    if ( !TeamCityHelper.TryGetTeamCitySourceWriteToken(
                            out var teamcitySourceWriteTokenEnvironmentVariableName,
                            out var teamcitySourceCodeWritingToken ) )
                    {
                        context.Console.WriteImportantMessage(
                            $"{teamcitySourceWriteTokenEnvironmentVariableName} environment variable is not set. Using default credentials." );
                    }
                    else
                    {
                        gitOrigin = gitOrigin.Insert( 8, $"teamcity%40postsharp.net:{teamcitySourceCodeWritingToken}@" );
                    }
                }
            }

            // Pushes tag to origin.
            if ( !ToolInvocationHelper.InvokeTool(
                    context.Console,
                    "git",
                    $"push {gitOrigin} {versionTag}",
                    context.RepoDirectory ) )
            {
                return false;
            }

            context.Console.WriteSuccess( $"Tagging the latest commit with version '{versionTag}' was successful." );

            return true;
        }

        private bool TryCommitVersionBump( BuildContext context, Version? currentVersion, Version newVersion, CommonCommandSettings settings )
        {
            // Adds bumped MainVersion.props and updated BumpInfo.txt to Git staging area.
            if ( !ToolInvocationHelper.InvokeTool(
                    context.Console,
                    "git",
                    $"add {this.MainVersionFilePath} {this.BumpInfoFilePath}",
                    context.RepoDirectory ) )
            {
                return false;
            }

            // Returns the remote origin.
            ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"remote get-url origin",
                context.RepoDirectory,
                out var gitExitCode,
                out var gitOrigin );

            if ( gitExitCode != 0 )
            {
                context.Console.WriteError( gitOrigin );

                return false;
            }

            gitOrigin = gitOrigin.Trim();
            var isHttps = gitOrigin.StartsWith( "https", StringComparison.InvariantCulture );

            // When on TeamCity, Git user credentials are set to TeamCity and if the repository is of HTTPS origin, the origin will be updated to form including Git authentication credentials.
            if ( TeamCityHelper.IsTeamCityBuild( settings ) )
            {
                if ( !TeamCityHelper.TrySetGitIdentityCredentials( context ) )
                {
                    return false;
                }

                if ( isHttps )
                {
                    if ( !TeamCityHelper.TryGetTeamCitySourceWriteToken(
                            out var teamcitySourceWriteTokenEnvironmentVariableName,
                            out var teamcitySourceCodeWritingToken ) )
                    {
                        context.Console.WriteImportantMessage(
                            $"{teamcitySourceWriteTokenEnvironmentVariableName} environment variable is not set. Using default credentials." );
                    }
                    else
                    {
                        gitOrigin = gitOrigin.Insert( 8, $"teamcity%40postsharp.net:{teamcitySourceCodeWritingToken}@" );
                    }
                }
            }

            if ( !ToolInvocationHelper.InvokeTool(
                    context.Console,
                    "git",
                    $"commit -m \"<<VERSION_BUMP>> {currentVersion?.ToString() ?? "unknown"} to {newVersion}\"",
                    context.RepoDirectory ) )
            {
                return false;
            }

            if ( !ToolInvocationHelper.InvokeTool(
                    context.Console,
                    "git",
                    $"push {gitOrigin}",
                    context.RepoDirectory ) )
            {
                return false;
            }

            return true;
        }

        private record PreparedVersionInfo( Version Version, string PackageVersionSuffix );

        /// <summary>
        /// Reads the MyProduct.version.props file from the artifacts directory generated by the Prepare step.
        /// </summary>
        private bool TryGetPreparedVersionInfo(
            BuildContext context,
            MainVersionFileInfo mainVersionFileInfo,
            [NotNullWhen( true )] out PreparedVersionInfo? preparedVersionInfo )
        {
            var overriddenPatchVersion = mainVersionFileInfo.OverriddenPatchVersion;

            string? mainVersion;
            Version? currentVersion;

            // The MainVersionDependency is not defined.
            if ( this.MainVersionDependency == null )
            {
                // The current version defaults to MainVersion.

                mainVersion = mainVersionFileInfo.MainVersion;

                currentVersion = Version.Parse( mainVersion );
            }
            else
            {
                // If MainVersionDependency and OverriddenPatchVersion properties are defined, we use OverriddenPatchVersion value.
                if ( !string.IsNullOrEmpty( overriddenPatchVersion ) )
                {
                    currentVersion = new Version( overriddenPatchVersion );
                }
                else
                {
                    // If no OverridenPatchVersion is defined, we use MainVersion property from private artifact.

                    var artifactVersionFile = Path.Combine(
                        context.RepoDirectory,
                        context.Product.PrivateArtifactsDirectory.ToString(),
                        context.Product.ProductName + ".version.props" );

                    var document = XDocument.Load( artifactVersionFile );
                    var project = document.Root;
                    var properties = project?.Element( "PropertyGroup" );
                    var propertyName = $"{context.Product.ProductNameWithoutDot}MainVersion";
                    mainVersion = properties?.Element( propertyName )?.Value;

                    if ( mainVersion == null )
                    {
                        context.Console.WriteError(
                            $"Cannot load '{this.MainVersionFilePath}': the property '{propertyName}' in '{artifactVersionFile}' is not defined." );

                        preparedVersionInfo = null;

                        return false;
                    }

                    // Set the current version to dependency version.
                    currentVersion = new Version( mainVersion );
                }
            }

            preparedVersionInfo = new PreparedVersionInfo( currentVersion, mainVersionFileInfo.PackageVersionSuffix );

            return true;
        }

        public virtual string GetNextBranchVersion( string originalBranchVersion )
        {
            var originalVersionParts = originalBranchVersion.Split( '.', 2 );

            return $"{originalVersionParts[0]}.{int.Parse( originalVersionParts[1], CultureInfo.InvariantCulture ) + 1}";
        }
    }
}