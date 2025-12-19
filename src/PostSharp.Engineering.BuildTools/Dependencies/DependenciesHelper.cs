// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Build.Evaluation;
using PostSharp.Engineering.BuildTools.Build;
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
        DependencyDefinition? GetDependencyDefinition( KeyValuePair<string, DependencySource> dependencyPair )
        {
            if ( !context.Product.TryGetDependencyDefinition( dependencyPair.Key, out var dependency ) )
            {
                context.Console.WriteWarning( $"The dependency '{dependencyPair.Key}' is not configured. Ignoring." );
            }

            return dependency;
        }

        var dependencies = dependenciesConfigurationFile
            .Dependencies
            .Where( d => d.Value.Origin != DependencyConfigurationOrigin.Transitive )
            .Select( d => (d.Value, GetDependencyDefinition( d )) )
            .Where( d => d.Item2 != null )
            .Select( d => new ResolvedDependency( d.Value, d.Item2! ) )
            .ToList();

        if ( dependencies.Count == 0 )
        {
            context.Console.WriteWarning( "No dependencies to fetch." );

            return true;
        }

        TeamCityClient? tc = null;
        var iterationDependencies = dependencies.ToImmutableDictionary( d => d.Dependency.Name, d => d );
        var dependencyDictionary = dependencies.ToImmutableDictionary( d => d.Dependency.Name, d => d );

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

            var transitiveDependencies = versionFile.Items.Where( i => i.ItemType == directDependency.Dependency.NameWithoutDot + "Dependencies" );

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

                if ( !context.Product.TryGetDependencyDefinition( name, out var dependencyDefinition ) )
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

                var newDependency = new ResolvedDependency( dependencySource, dependencyDefinition );
                newDependenciesBuilder.Add( newDependency.Dependency.Name, newDependency );
                dependenciesConfigurationFile.Dependencies[name] = dependencySource;
            }

            ProjectCollection.GlobalProjectCollection.UnloadAllProjects();
        }

        newDependencies = newDependenciesBuilder.ToImmutable();

        return true;
    }

    private static bool GetLatestBuildId(
        ConsoleHelper console,
        TeamCityClient teamCity,
        string dependencyName,
        string ciBuildType,
        string branch,
        [NotNullWhen( true )] out CiBuildId? latestBuildId )
    {
        latestBuildId = teamCity.GetLatestBuildId(
            console,
            ciBuildType,
            branch );

        if ( latestBuildId == null )
        {
            console.WriteError( $"No successful build for '{dependencyName}' dependency on '{branch}' branch, BuildTypeId='{ciBuildType}'." );

            return false;
        }

        return true;
    }

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

                if ( !GetLatestBuildId(
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
                    artifactsDirectory ) )
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
            if ( dependency.Source.VersionFile == null )
            {
                dependency.Source.VersionFile =
                    Path.Combine(
                        dependency.Source.GetResolvedLocalPath( context, dependency.Dependency.Name ),
                        dependency.Dependency.Name + ".Import.props" );
            }

            if ( !File.Exists( dependency.Source.VersionFile ) )
            {
                context.Console.WriteError( $"The file '{dependency.Source.VersionFile}' does not exist. Check that the product has been built." );

                return false;
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

            if ( dependency.Source.VersionFile == null )
            {
                var path = Path.Combine( context.RepoDirectory, "dependencies", dependency.Dependency.Name, $"{dependency.Dependency.Name}.version.props" );
                dependency.Source.VersionFile = path;
            }

            if ( !File.Exists( dependency.Source.VersionFile ) )
            {
                context.Console.WriteError( $"The following artifact was not restored by TeamCity: '{dependency.Source.VersionFile}'" );

                return false;
            }

            var document = XDocument.Load( dependency.Source.VersionFile );

            var buildNumber = document.Root!.XPathSelectElement( $"/Project/PropertyGroup/{dependency.Dependency.NameWithoutDot}BuildNumber" )
                ?.Value;

            var buildType = document.Root!.XPathSelectElement( $"/Project/PropertyGroup/{dependency.Dependency.NameWithoutDot}BuildType" )?.Value;

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
        string artifactsPath )
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

        dependencySource.VersionFile = versionFile;

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

    private record ResolvedDependency( DependencySource Source, DependencyDefinition Dependency );
}