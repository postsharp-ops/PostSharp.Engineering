// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Build.Evaluation;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;

namespace PostSharp.Engineering.BuildTools.Dependencies;

internal static class DependenciesHelper
{
    public static bool UpdateOrFetchDependencies(
        BuildContext context,
        BuildConfiguration configuration,
        DependenciesConfigurationFile dependenciesConfigurationFile,
        bool update )
    {
        (DependencyDefinition? Definition, ParametrizedDependency? Parametrized) GetDependencyInfo( KeyValuePair<string, DependencySource> dependencyPair )
        {
            if ( context.Product.TryGetDependency( dependencyPair.Key, out var parametrizedDependency ) )
            {
                return (parametrizedDependency.Definition, parametrizedDependency);
            }

            if ( context.Product.TryGetDependencyDefinition( dependencyPair.Key, out var dependency ) )
            {
                return (dependency, null);
            }

            context.Console.WriteWarning( $"The dependency '{dependencyPair.Key}' is not configured. Ignoring." );

            return (null, null);
        }

        var dependencies = dependenciesConfigurationFile
            .Dependencies
            .Where( d => d.Value.Origin != DependencyConfigurationOrigin.Transitive )
            .Select( d => (Pair: d, Info: GetDependencyInfo( d )) )
            .Where( d => d.Info.Definition != null )
            .Select( d => new ResolvedDependency( d.Pair.Value, d.Info.Definition!, d.Info.Parametrized ) )
            .ToList();

        if ( dependencies.Count == 0 )
        {
            context.Console.WriteWarning( "No dependencies to fetch." );

            return true;
        }

        TeamCityClient? tc = null;
        var iterationDependencies = dependencies.ToImmutableDictionary( d => d.Key, d => d );
        var dependencyDictionary = dependencies.ToImmutableDictionary( d => d.Key, d => d );

        while ( iterationDependencies.Count > 0 )
        {
            // Don't try to connect to TeamCity if no dependency is a build server dependency
            // to allow for build without access to TeamCity.
            if ( tc == null && iterationDependencies.Values.Any( d => d.Source.SourceKind == DependencySourceKind.BuildServer ) )
            {
                if ( !TeamCityHelper.TryConnectTeamCity( context, out tc ) )
                {
                    return false;
                }
            }

            if ( tc != null && !ResolveBuildNumbersFromBranches( context, configuration, tc, iterationDependencies, update ) )
            {
                return false;
            }

            if ( !ResolveLocalDependencies( context, iterationDependencies ) ||
                 !ResolveRestoredDependencies( context, iterationDependencies ) )
            {
                return false;
            }

            // Download build server dependencies.
            if ( tc != null && !DownloadArtifacts( context, tc, iterationDependencies ) )
            {
                return false;
            }

            // Find implicit transitive dependencies.
            if ( !TryGetTransitiveDependencies(
                    context,
                    dependencyDictionary,
                    iterationDependencies,
                    dependenciesConfigurationFile,
                    out var newDependencies ) )
            {
                return false;
            }

            iterationDependencies = newDependencies;
            dependencyDictionary = dependencyDictionary.AddRange( newDependencies );
        }

        context.Console.WriteSuccess( $"{(update ? "Updating" : "Fetching")} build artifacts was successful" );

        return true;
    }

    private static bool TryGetTransitiveDependencies(
        BuildContext context,
        ImmutableDictionary<string, ResolvedDependency> allDependencies,
        ImmutableDictionary<string, ResolvedDependency> directDependencies,
        DependenciesConfigurationFile dependenciesConfigurationFile,
        [NotNullWhen( true )] out ImmutableDictionary<string, ResolvedDependency>? newDependencies )
    {
        var newDependenciesBuilder = ImmutableDictionary.CreateBuilder<string, ResolvedDependency>();

        newDependencies = null;

        foreach ( var directDependency in directDependencies.Values )
        {
            if ( directDependency.Source.SourceKind == DependencySourceKind.Feed )
            {
                // The dependency is managed by NuGet.
                // Currently, we don't support retrieving transitive dependencies from NuGet packages.
                continue;
            }

            var versionFile = Project.FromFile( directDependency.Source.VersionFile!, MSBuildLoadOptions.IgnoreImportErrors );

            // Item type uses KeyWithoutDot — for aliased deps the transform renamed `<MetalamaDependencies>` to `<Metalama20260Dependencies>`.
            var transitiveDependencies = versionFile.Items.Where( i => i.ItemType == directDependency.KeyWithoutDot + "Dependencies" );

            foreach ( var transitiveDependency in transitiveDependencies )
            {
                var name = transitiveDependency.EvaluatedInclude;

                if ( newDependenciesBuilder.ContainsKey( name ) )
                {
                    // This dependency is transitively included twice through different paths.
                    continue;
                }

                var sourceKindString = transitiveDependency.GetMetadata( "SourceKind" )?.EvaluatedValue;

                if ( sourceKindString == null || !Enum.TryParse<DependencySourceKind>( sourceKindString, out var sourceKind ) )
                {
                    context.Console.WriteWarning( $"Cannot parse the source kind '{sourceKindString}' in '{directDependency.Source.VersionFile}'." );

                    continue;
                }

                if ( allDependencies.TryGetValue( name, out _ ) )
                {
                    continue;
                }

                // Resolve the transitive dep's definition starting from the direct dep's product family. For aliased direct
                // deps (e.g., Metalama 2026.0 aliased into a Metalama.Vsx 2026.1 build) this is essential — the consumer's
                // family chain only includes the *current* version of the same logical product family (V2026_1), so a
                // consumer-rooted lookup of "Metalama.Compiler" would find V2026_1.MetalamaCompiler whose CiConfiguration
                // has 2026.1 build type IDs that don't match the 2026.0 buildId stored in the producer's version.props.
                // For unaliased direct deps the direct dep's family is typically the same as (or relative-included by) the
                // consumer's, so this preserves existing behavior.
                if ( !directDependency.Dependency.ProductFamily.TryGetDependencyDefinition( name, out var dependencyDefinition )
                     && !context.Product.TryGetDependencyDefinition( name, out dependencyDefinition ) )
                {
                    context.Console.WriteError(
                        $"Cannot find the dependency definition for '{name}' referenced by '{directDependency.Dependency.Name}'. The dependency must be defined in PostSharp.Engineering." );

                    return false;
                }

                // Create a DependencySource.
                DependencySource dependencySource;

                bool TryGetBuildId( [NotNullWhen( true )] out CiBuildId? ciBuildId )
                {
                    var buildNumber = transitiveDependency.GetMetadataValue( "BuildNumber" );
                    var ciBuildTypeId = transitiveDependency.GetMetadataValue( "CiBuildTypeId" );

                    if ( string.IsNullOrEmpty( buildNumber ) || string.IsNullOrEmpty( ciBuildTypeId ) )
                    {
                        context.Console.WriteError(
                            $"The dependency '{name}' must have both BuildNumber and CiBuildTypeId properties in {directDependency.Source.VersionFile}." );

                        ciBuildId = null;

                        return false;
                    }

                    ciBuildId = new CiBuildId( int.Parse( buildNumber, CultureInfo.InvariantCulture ), ciBuildTypeId );

                    return true;
                }

                // If we build locally, we need to consider transitive restored dependencies as build server dependencies,
                // as dependencies are restored by CI on build agents only. Locally, all dependencies are downloaded by PostSharp.Engineering.
                if ( sourceKind == DependencySourceKind.RestoredDependency && directDependency.Source.SourceKind != DependencySourceKind.RestoredDependency )
                {
                    sourceKind = DependencySourceKind.BuildServer;
                }

                switch ( sourceKind )
                {
                    case DependencySourceKind.BuildServer:
                        {
                            if ( !TryGetBuildId( out var buildId ) )
                            {
                                return false;
                            }

                            dependencySource = DependencySource.CreateBuildServerSource(
                                buildId,
                                DependencyConfigurationOrigin.Transitive );
                        }

                        break;

                    case DependencySourceKind.Local:
                        {
                            var localPath = transitiveDependency.GetMetadata( "Path" )?.EvaluatedValue;
                            dependencySource = DependencySource.CreateLocalDependency( DependencyConfigurationOrigin.Transitive, localPath );

                            break;
                        }

                    case DependencySourceKind.RestoredDependency:
                        {
                            if ( !TryGetBuildId( out var buildId ) )
                            {
                                return false;
                            }

                            if ( buildId.BuildTypeId == null )
                            {
                                context.Console.WriteError( $"Unknown build type ID of '{dependencyDefinition.Name}' transitive restored dependency." );
                            }

                            dependencySource = DependencySource.CreateRestoredDependency( buildId, DependencyConfigurationOrigin.Transitive );
                        }

                        break;

                    case DependencySourceKind.Feed:
                        var version = transitiveDependency.GetMetadata( "Version" )?.EvaluatedValue;

                        if ( string.IsNullOrEmpty( version ) )
                        {
                            context.Console.WriteError( $"The dependency '{name}' must have a Version property in {directDependency.Source.VersionFile}." );

                            return false;
                        }

                        dependencySource = DependencySource.CreateFeed( version, DependencyConfigurationOrigin.Transitive );

                        break;

                    default:
                        throw new InvalidOperationException();
                }

                // Transitive deps are not declared at the consumer's use site, so they have no ParametrizedDependency / alias.
                var newDependency = new ResolvedDependency( dependencySource, dependencyDefinition, Parametrized: null );
                newDependenciesBuilder.Add( newDependency.Key, newDependency );
                dependenciesConfigurationFile.Dependencies[name] = dependencySource;
            }

            ProjectCollection.GlobalProjectCollection.UnloadAllProjects();
        }

        newDependencies = newDependenciesBuilder.ToImmutable();

        return true;
    }

    private static bool TryGetLatestBuildId(
        ConsoleHelper console,
        TeamCityClient teamCity,
        string dependencyName,
        string ciBuildType,
        string branch,
        [NotNullWhen( true )] out CiBuildId? latestBuildId )
        => teamCity.TryGetLatestBuildId( console, ciBuildType, branch, out latestBuildId );

    private static bool ResolveBuildNumbersFromBranches(
        BuildContext context,
        BuildConfiguration configuration,
        TeamCityClient teamCity,
        ImmutableDictionary<string, ResolvedDependency> dependencies,
        bool update )
    {
        foreach ( var dependency in dependencies.Values )
        {
            if ( dependency.Source.SourceKind != DependencySourceKind.BuildServer )
            {
                continue;
            }

            var buildSpec = dependency.Source.BuildServerSource;
            var buildId = buildSpec as CiBuildId;
            CiBuildId resolvedBuildId;

            if ( buildId != null && !update )
            {
                resolvedBuildId = buildId;
            }
            else
            {
                string ciBuildType;
                string branchName;

                if ( buildSpec is CiLatestBuildOfBranch branch )
                {
                    BuildConfiguration dependencyConfiguration;

                    if ( context.Product.TryGetDependency( dependency.Dependency.Name, out var parametrizedDependency ) )
                    {
                        dependencyConfiguration = parametrizedDependency.ConfigurationMapping[configuration];
                    }
                    else
                    {
                        context.Console.WriteError(
                            $"The source of the transitive dependency '{dependency.Dependency.Name}' is set to CiLatestBuildOfBranch. This is allowed only for direct dependencies." );

                        return false;
                    }

                    ciBuildType = dependency.Dependency.CiConfiguration.BuildTypes[dependencyConfiguration];
                    branchName = branch.Name;
                }
                else if ( buildId != null )
                {
                    // We already have a resolved reference, but we need to update.
                    // In this case, we do not change the BuildIdType.

                    ciBuildType = buildId.BuildTypeId ?? dependency.Dependency.CiConfiguration.BuildTypes[configuration];

                    if ( !teamCity.TryGetBranchFromBuildNumber( context.Console, buildId, out var previousBranchName ) )
                    {
                        return false;
                    }

                    // Normalize the branch prefix.
                    const string prefix = "refs/heads/";

                    if ( previousBranchName.StartsWith( prefix, StringComparison.OrdinalIgnoreCase )
                         && !dependency.Dependency.Branch.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
                    {
                        previousBranchName = previousBranchName.Substring( prefix.Length );
                    }

                    branchName = previousBranchName;
                }
                else
                {
                    ciBuildType = dependency.Dependency.CiConfiguration.BuildTypes[configuration];
                    branchName = dependency.Dependency.Branch;
                }

                if ( !TryGetLatestBuildId(
                        context.Console,
                        teamCity,
                        dependency.Dependency.Name,
                        ciBuildType,
                        branchName,
                        out var latestBuildId ) )
                {
                    return false;
                }

                resolvedBuildId = latestBuildId;
            }

            dependency.Source.BuildServerSource = resolvedBuildId;
        }

        return true;
    }

    private static bool DownloadArtifacts(
        BuildContext context,
        TeamCityClient teamCity,
        ImmutableDictionary<string, ResolvedDependency> dependencies )
    {
        foreach ( var dependency in dependencies.Values )
        {
            if ( dependency.Source.SourceKind != DependencySourceKind.BuildServer )
            {
                // No need to download.
                continue;
            }

            if ( dependency.Source.BuildServerSource is not CiBuildId buildId )
            {
                // The dependency has not been resolved yet.
                continue;
            }

            if ( buildId.BuildTypeId == null )
            {
                context.Console.WriteError( $"The dependency '{dependency.Dependency.Name}' does not have a Teamcity build type ID set." );

                return false;
            }

            // We don't store the configuration of the dependency, but we can know it given the CI build type id because of the one-to-one mapping.
            var buildTypes = dependency.Dependency.CiConfiguration.BuildTypes.AsDictionary();
            var dependencyConfigurations = buildTypes.Where( x => x.Value == buildId.BuildTypeId ).ToList();

            if ( dependencyConfigurations.Count != 1 )
            {
                context.Console.WriteError(
                    $"Expected 1 build configuration of dependency '{dependency.Dependency.Name}' with CI build type equal to '{buildId.BuildTypeId}', but got {dependencyConfigurations.Count}."
                    +
                    $"The configured CI build types are: " + string.Join( ", ", buildTypes.Select( x => x.Value ) ) );

                return false;
            }

            var artifactsDirectory = dependency.Dependency.GetPrivateArtifactsDirectory( buildId.BuildConfiguration );

            if ( !DownloadDependency(
                    context,
                    teamCity,
                    dependency.Source,
                    dependency.Dependency.Name,
                    buildId.BuildTypeId,
                    buildId.BuildNumber,
                    artifactsDirectory,
                    dependency.IsAliased ? dependency : null ) )
            {
                return false;
            }
        }

        return true;
    }

    private static bool ResolveLocalDependencies( BuildContext context, ImmutableDictionary<string, ResolvedDependency> dependencies )
    {
        foreach ( var dependency in dependencies.Values.Where( d => d.Source.SourceKind is DependencySourceKind.Local ) )
        {
            // Producer's local .Import.props lives in the producer's repo root, named after the producer's product.
            var producerName = dependency.Dependency.Name;

            var producerImportPath = Path.Combine(
                dependency.Source.GetResolvedLocalPath( context, producerName ),
                producerName + ".Import.props" );

            if ( !File.Exists( producerImportPath ) )
            {
                context.Console.WriteError( $"The file '{producerImportPath}' does not exist. Check that the product has been built." );

                return false;
            }

            if ( dependency.IsAliased )
            {
                // Resolve the .version.props that the producer's .Import.props points at, then transform it
                // into an alias-prefixed copy under dependencies/{Key}/.
                var importDocument = XDocument.Load( producerImportPath );
                var importElement = importDocument.Descendants( "Import" ).FirstOrDefault();
                var producerVersionPropsRelative = importElement?.Attribute( "Project" )?.Value;

                if ( producerVersionPropsRelative == null )
                {
                    context.Console.WriteError( $"Cannot read <Import Project=\"...\"/> from '{producerImportPath}'." );

                    return false;
                }

                var producerVersionPropsAbsolute = Path.GetFullPath(
                    Path.Combine( Path.GetDirectoryName( producerImportPath )!, producerVersionPropsRelative ) );

                if ( !File.Exists( producerVersionPropsAbsolute ) )
                {
                    context.Console.WriteError( $"The file '{producerVersionPropsAbsolute}' does not exist." );

                    return false;
                }

                var aliasDirectory = TeamCityHelper.GetRestoredDependencyDirectory( context.RepoDirectory, dependency.Key );
                var aliasedVersionPropsPath = Path.Combine( aliasDirectory, dependency.Key + ".version.props" );

                TransformVersionPropsForAlias(
                    producerVersionPropsAbsolute,
                    aliasedVersionPropsPath,
                    dependency.Dependency.NameWithoutDot,
                    dependency.KeyWithoutDot );

                WriteAliasImportFile( aliasDirectory, dependency.Key );

                dependency.Source.VersionFile = Path.Combine( aliasDirectory, dependency.Key + ".Import.props" );
            }
            else
            {
                dependency.Source.VersionFile = producerImportPath;
            }
        }

        return true;
    }

    private static bool ResolveRestoredDependencies( BuildContext context, ImmutableDictionary<string, ResolvedDependency> dependencies )
    {
        foreach ( var dependency in dependencies.Values.Where( d => d.Source.SourceKind is DependencySourceKind.RestoredDependency ) )
        {
            if ( dependency.Source.SourceKind != DependencySourceKind.RestoredDependency )
            {
                continue;
            }

            // For aliased deps, TeamCity's artifact rule (generated by ConfigurationProperties.cs) restores the producer's
            // {Name}.version.props to dependencies/{Key}/. We then transform it in place to {Key}.version.props.
            if ( dependency.IsAliased )
            {
                var aliasDirectory = TeamCityHelper.GetRestoredDependencyDirectory( context.RepoDirectory, dependency.Key );
                var producerRestoredPath = Path.Combine( aliasDirectory, dependency.Dependency.Name + ".version.props" );
                var aliasedVersionPropsPath = Path.Combine( aliasDirectory, dependency.Key + ".version.props" );

                if ( File.Exists( producerRestoredPath ) && !File.Exists( aliasedVersionPropsPath ) )
                {
                    TransformVersionPropsForAlias(
                        producerRestoredPath,
                        aliasedVersionPropsPath,
                        dependency.Dependency.NameWithoutDot,
                        dependency.KeyWithoutDot );
                }

                if ( dependency.Source.VersionFile == null )
                {
                    dependency.Source.VersionFile = aliasedVersionPropsPath;
                }
            }
            else if ( dependency.Source.VersionFile == null )
            {
                var path = TeamCityHelper.GetRestoredDependencyVersionFile( context.RepoDirectory, dependency.Key );
                dependency.Source.VersionFile = path;
            }

            if ( !File.Exists( dependency.Source.VersionFile ) )
            {
                context.Console.WriteError( $"The following artifact was not restored by TeamCity: '{dependency.Source.VersionFile}'" );

                return false;
            }

            var document = XDocument.Load( dependency.Source.VersionFile );

            // BuildNumber/BuildType use KeyWithoutDot — for aliased deps the transform renamed them.
            var buildNumber = document.Root!.XPathSelectElement( $"/Project/PropertyGroup/{dependency.KeyWithoutDot}BuildNumber" )
                ?.Value;

            var buildType = document.Root!.XPathSelectElement( $"/Project/PropertyGroup/{dependency.KeyWithoutDot}BuildType" )?.Value;

            if ( !string.IsNullOrEmpty( buildNumber ) && !string.IsNullOrEmpty( buildType ) )
            {
                dependency.Source.BuildServerSource = new CiBuildId( int.Parse( buildNumber, CultureInfo.InvariantCulture ), buildType );
            }
        }

        return true;
    }

    private static bool DownloadBuild(
        BuildContext context,
        TeamCityClient teamCity,
        string dependencyName,
        string ciBuildTypeId,
        int buildNumber,
        string artifactsPath,
        out string restoreDirectory )
    {
        restoreDirectory = Path.Combine(
            Environment.GetEnvironmentVariable( "USERPROFILE" ) ?? Path.GetTempPath(),
            ".build-artifacts",
            dependencyName,
            ciBuildTypeId,
            buildNumber.ToString( CultureInfo.InvariantCulture ) );

        var completedFile = Path.Combine( restoreDirectory, ".completed" );

        if ( !File.Exists( completedFile ) )
        {
            if ( Directory.Exists( restoreDirectory ) )
            {
                Directory.Delete( restoreDirectory, true );
            }

            Directory.CreateDirectory( restoreDirectory );
            context.Console.WriteMessage( $"Downloading {dependencyName} build #{buildNumber} of {ciBuildTypeId}" );

            if ( !teamCity.TryDownloadArtifacts( context.Console, ciBuildTypeId, buildNumber, artifactsPath, restoreDirectory, !context.Settings.NoProgress ) )
            {
                return false;
            }

            File.WriteAllText( completedFile, "Completed" );
        }
        else
        {
            context.Console.WriteMessage( $"Dependency '{dependencyName}' is up to date: build #{buildNumber} of {ciBuildTypeId} was already downloaded." );
        }

        return true;
    }

    private static bool DownloadDependency(
        BuildContext context,
        TeamCityClient teamCity,
        DependencySource dependencySource,
        string dependencyName,
        string ciBuildTypeId,
        int buildNumber,
        string artifactsPath,
        ResolvedDependency? aliasedDependency )
    {
        if ( !DownloadBuild( context, teamCity, dependencyName, ciBuildTypeId, buildNumber, artifactsPath, out var restoreDirectory ) )
        {
            return false;
        }

        // Find the version file.
        var versionFile = FindVersionFile( dependencyName, restoreDirectory );

        if ( versionFile == null )
        {
            context.Console.WriteError( $"Could not find {dependencyName}.version.props under '{restoreDirectory}'." );

            return false;
        }

        if ( aliasedDependency != null )
        {
            // Write a transformed copy alongside the original; the import target is the transformed one.
            var aliasedVersionFile = Path.Combine(
                Path.GetDirectoryName( versionFile )!,
                aliasedDependency.Key + ".version.props" );

            TransformVersionPropsForAlias(
                versionFile,
                aliasedVersionFile,
                aliasedDependency.Dependency.NameWithoutDot,
                aliasedDependency.KeyWithoutDot );

            dependencySource.VersionFile = aliasedVersionFile;
        }
        else
        {
            dependencySource.VersionFile = versionFile;
        }

        return true;
    }

    private static string? FindVersionFile( string productName, string directory )
    {
        var path = Path.Combine( directory, productName + ".version.props" );

        if ( File.Exists( path ) )
        {
            return path;
        }

        foreach ( var subdirectory in Directory.GetDirectories( directory ) )
        {
            path = FindVersionFile( productName, subdirectory );

            if ( path != null )
            {
                return path;
            }
        }

        return null;
    }

    private record ResolvedDependency( DependencySource Source, DependencyDefinition Dependency, ParametrizedDependency? Parametrized )
    {
        /// <summary>
        /// Gets the consumer-side key: <see cref="ParametrizedDependency.Key"/> if available, otherwise <see cref="DependencyDefinition.Name"/>.
        /// </summary>
        public string Key => this.Parametrized?.Key ?? this.Dependency.Name;

        /// <summary>
        /// Gets <see cref="Key"/> with dots removed.
        /// </summary>
        public string KeyWithoutDot => this.Parametrized?.KeyWithoutDot ?? this.Dependency.NameWithoutDot;

        /// <summary>
        /// Whether the consumer-side key differs from the definition name (i.e., an alias is in effect).
        /// </summary>
        public bool IsAliased => this.Parametrized?.Alias != null;
    }

    /// <summary>
    /// Element-name suffixes emitted by <c>ArtifactManifestFile.TryWrite</c> as producer-prefixed properties or item types.
    /// Used by <see cref="TransformVersionPropsForAlias"/> to identify which elements to rename when an aliased dep
    /// is consumed. Other elements (transitive Feed dep version properties, item metadata, etc.) are left alone.
    /// </summary>
    internal static readonly string[] ProducerPropertySuffixes =
    {
        "",                                  // bare prefix (rarely used; included for completeness)
        "Version",
        "MainVersion",
        "PreviewVersion",
        "AssemblyVersion",
        "VersionPrefix",
        "VersionSuffix",
        "VersionPatchNumber",
        "VersionWithoutSuffix",
        "BuildConfiguration",
        "BuildNumber",
        "BuildType",
        "BuildDate",
        "Dependencies",
        "ArtifactsDirectory",
        "PublicArtifactsDirectory",
        "PrivateArtifactsDirectory",
        "EngineeringVersion",
        "VersionFilePath"
    };

    /// <summary>
    /// Transforms a producer-published <c>{ProducerName}.version.props</c> (or <c>.Import.props</c>) into a copy whose
    /// producer-prefixed property/item names are replaced with the consumer's alias prefix. Used so that two references
    /// to the same logical product (e.g., Metalama 2026.1 and Metalama 2026.0) under different aliases can coexist in
    /// the consumer's MSBuild scope without colliding on properties like <c>$(MetalamaVersion)</c>.
    /// </summary>
    /// <param name="sourceFile">Source file path.</param>
    /// <param name="destinationFile">Destination file path. Parent directory is created if missing.</param>
    /// <param name="oldPrefix">Producer's <c>NameWithoutDot</c> (e.g., <c>Metalama</c>).</param>
    /// <param name="newPrefix">Consumer's <c>KeyWithoutDot</c> (e.g., <c>Metalama20260</c>).</param>
    /// <remarks>
    /// Renames only elements whose local name matches <c>{oldPrefix}{Suffix}</c> where <c>Suffix</c> is in
    /// <see cref="ProducerPropertySuffixes"/>. This conservative rule avoids accidentally renaming transitive dep version
    /// properties such as <c>MetalamaCompilerVersion</c> (whose suffix <c>CompilerVersion</c> is not in the list).
    /// Also absolutizes any relative <c>&lt;Import Project="..."/&gt;</c> path against the source file's directory so the
    /// relocated copy still resolves.
    /// </remarks>
    internal static void TransformVersionPropsForAlias( string sourceFile, string destinationFile, string oldPrefix, string newPrefix )
    {
        var document = XDocument.Load( sourceFile );
        var sourceDirectory = Path.GetDirectoryName( sourceFile )!;

        foreach ( var element in document.Descendants().ToList() )
        {
            var localName = element.Name.LocalName;

            if ( localName.StartsWith( oldPrefix, StringComparison.Ordinal ) )
            {
                var remainder = localName.Substring( oldPrefix.Length );

                if ( Array.IndexOf( ProducerPropertySuffixes, remainder ) >= 0 )
                {
                    element.Name = element.Name.Namespace + (newPrefix + remainder);
                }
            }

            if ( element.Name.LocalName == "Import" )
            {
                var projectAttribute = element.Attribute( "Project" );

                if ( projectAttribute != null && !Path.IsPathRooted( projectAttribute.Value ) )
                {
                    projectAttribute.Value = Path.GetFullPath( Path.Combine( sourceDirectory, projectAttribute.Value ) );
                }

                var conditionAttribute = element.Attribute( "Condition" );

                if ( conditionAttribute != null )
                {
                    // Conditions like "Exists('../foo.props')" need the path absolutized too.
                    var match = System.Text.RegularExpressions.Regex.Match( conditionAttribute.Value, @"Exists\s*\(\s*'([^']+)'\s*\)" );

                    if ( match.Success )
                    {
                        var pathInCondition = match.Groups[1].Value;

                        if ( !Path.IsPathRooted( pathInCondition ) )
                        {
                            var absolutePath = Path.GetFullPath( Path.Combine( sourceDirectory, pathInCondition ) );
                            conditionAttribute.Value = conditionAttribute.Value.Replace( pathInCondition, absolutePath, StringComparison.Ordinal );
                        }
                    }
                }
            }
        }

        Directory.CreateDirectory( Path.GetDirectoryName( destinationFile )! );
        document.Save( destinationFile );
    }

    /// <summary>
    /// Writes a thin <c>{Key}.Import.props</c> file at <paramref name="aliasDirectory"/> that imports the alongside
    /// <c>{Key}.version.props</c>. This keeps the consumer's import-file convention (one <c>.Import.props</c> entry point
    /// per dep) while still pointing at the transformed version.props.
    /// </summary>
    private static void WriteAliasImportFile( string aliasDirectory, string key )
    {
        var importFilePath = Path.Combine( aliasDirectory, key + ".Import.props" );
        var versionPropsRelative = key + ".version.props";

        var content = $@"<!-- File generated by PostSharp.Engineering, method {nameof(DependenciesHelper)}.{nameof(WriteAliasImportFile)}, for aliased dependency '{key}'. -->
<Project>
    <Import Project=""$(MSBuildThisFileDirectory){versionPropsRelative}"" Condition=""Exists( '$(MSBuildThisFileDirectory){versionPropsRelative}' )""/>
</Project>
";

        Directory.CreateDirectory( aliasDirectory );
        File.WriteAllText( importFilePath, content );
    }
}