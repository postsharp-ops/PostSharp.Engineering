// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Extensions.FileSystemGlobbing;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Model;
using System.Net;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.BillOfMaterials;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

internal static class DependencyWalker
{
    private static readonly DependentPackageInfoOverride[] _defaultDependentPackageInfoOverrides =
    [
        new() { Name = "ComparerExtensions", License = "Public domain" },
        new() { Name = "ILRepack", License = "Apache-2.0", UsageKind = DependentPackageUsageKind.Private },
        new() { Name = "LibGit2Sharp", License = "MIT" },
        new() { Name = "LINQPad", License = "Proprietary" },
        new() { Name = "xunit", License = "Apache-2.0", RepositoryUrl = "https://github.com/xunit/xunit" },

        // Analyzers are set as Private unless they flow to the consumer.
        new() { Name = "StyleCop.Analyzers", UsageKind = DependentPackageUsageKind.Private },
        new() { Name = "xunit.analyzers", UsageKind = DependentPackageUsageKind.Private }
    ];

    public static readonly DependentPackageExclusion[] DefaultDependentPackageExclusions =
    [
        new( "System.", "System" ),
        new( "Microsoft.", "System" ),
        new( "NETStandard.", "System" ),
        new( "Runtime.", "System" )
    ];

    private static IReadOnlyList<PackageDependencyInfo> GetPackageDependencies( BuildContext context )
    {
        var defaultConfiguration = ConfigurationNeutralVersionFile.ReadDefaultConfiguration( context ) ?? BuildConfiguration.Debug;

        var list = new List<PackageDependencyInfo>();

        var depsFiles = new List<FilePatternMatch>();

        var depsFilesPattern = Pattern.Create( "**/$(Configuration)/**/*.deps.json" )
            .Remove( "**/tests/*.deps.json" )
            .Remove( "eng/**/*.deps.json" )
            .Append( context.Product.ConsumableDepsFiles );

        if ( !depsFilesPattern.TryGetFiles(
                context.RepoDirectory,
                new BuildArguments( null, defaultConfiguration, context.Product, null ),
                depsFiles ) )
        {
            return [];
        }

        foreach ( var depsFile in depsFiles )
        {
            context.Console.WriteMessage( $"Processing {depsFile.Path}..." );
            var projectName = Path.GetFileName( depsFile.Path ).Replace( ".deps.json", "", StringComparison.OrdinalIgnoreCase );

            try
            {
                var depsContent = JsonNode.Parse( File.ReadAllText( depsFile.Path ) );
                var libraries = depsContent?["libraries"]?.AsObject();

                if ( libraries != null )
                {
                    foreach ( var library in libraries )
                    {
                        if ( library.Value == null )
                        {
                            continue;
                        }

                        var type = library.Value["type"]?.ToString();

                        if ( type != "package" )
                        {
                            continue;
                        }

                        var parts = library.Key.Split( '/' );

                        if ( parts.Length == 2 )
                        {
                            var packageName = parts[0];
                            var version = parts[1];

                            list.Add( new PackageDependencyInfo( projectName, packageName, version ) );
                        }
                    }
                }
            }
            catch ( Exception ex )
            {
                context.Console.WriteWarning( $"Failed to process {depsFile}. Exception: {ex.Message} Skipping." );
            }
        }

        return list;
    }

    private static async Task<IReadOnlyCollection<PackageInfo>> GetPackageInfoAsync( BuildContext context, IReadOnlyList<PackageDependencyInfo> dependencies )
    {
        var httpClientHandler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };

        using var httpClient = new HttpClient( httpClientHandler );
        var packageInfoMap = new Dictionary<string, PackageInfo>();

        foreach ( var dependency in dependencies )
        {
            var packageName = dependency.PackageName;
            var version = dependency.PackageVersion;

            var projectUsageInfo = context.Product.ProjectUsages
                .LastOrDefault( x => Regex.IsMatch( dependency.ProjectName, x.Pattern, RegexOptions.IgnoreCase ) );

            var usageKind = projectUsageInfo?.Kind ?? DependentPackageUsageKind.Default;

            if ( DefaultDependentPackageExclusions
                .Concat( context.Product.DependentPackageExclusions )
                .Any( exclusion => packageName.StartsWith( exclusion.Namespace, StringComparison.OrdinalIgnoreCase ) ) )
            {
                continue;
            }

            if ( !packageInfoMap.TryGetValue( packageName, out var packageInfo ) )
            {
                packageInfo = new PackageInfo() { Name = packageName };
                packageInfoMap[packageName] = packageInfo;
            }

            var baseUrl = $"https://api.nuget.org/v3/registration5-gz-semver2/{packageName.ToLowerInvariant()}/{version.ToLowerInvariant()}.json";
            var attempts = 0;
            const int maxAttempts = 10;

            if ( !packageInfo.Versions.TryGetValue( version, out var packageVersionInfo ) )
            {
                while ( attempts <= maxAttempts )
                {
                    attempts++;

                    try
                    {
                        Console.WriteLine( $"Fetching {baseUrl} for version {version}..." );
                        var versionEntryResponse = await httpClient.GetStringAsync( baseUrl );
                        var versionEntryJson = JsonNode.Parse( versionEntryResponse );

                        if ( versionEntryJson == null )
                        {
                            context.Console.WriteWarning( $"'{baseUrl}' returned null." );

                            continue;
                        }

                        var catalogueEntryUrl = versionEntryJson["catalogEntry"]?.ToString();

                        if ( catalogueEntryUrl == null )
                        {
                            context.Console.WriteWarning( $"'{baseUrl}': cannot find the catalogueEntry." );

                            continue;
                        }

                        Console.WriteLine( $"Fetching {catalogueEntryUrl} for version {version}..." );
                        var catelogueEntryResponse = await httpClient.GetStringAsync( catalogueEntryUrl );
                        var catalogueEntryJson = JsonNode.Parse( catelogueEntryResponse );

                        packageVersionInfo = new PackageVersionInfo()
                        {
                            Version = version,
                            License = catalogueEntryJson?["licenseExpression"]?.ToString(),
                            Owners = catalogueEntryJson?["authors"]?.ToString(),
                            SourceRepository = catalogueEntryJson?["projectUrl"]?.ToString()
                        };

                        packageInfo.Versions[version] = packageVersionInfo;

                        break;
                    }
                    catch ( HttpRequestException ex ) when ( ex.StatusCode != HttpStatusCode.NotFound )
                    {
                        if ( attempts < maxAttempts )
                        {
                            context.Console.WriteWarning(
                                $"Failed to fetch package information for {packageName} version {version}. Retrying... ({attempts}/{maxAttempts}). Exception: {ex.Message}" );

                            await Task.Delay( 15000 );
                        }
                        else
                        {
                            context.Console.WriteError(
                                $"Failed to fetch package information for {packageName} version {version} after {maxAttempts} attempts. Exception: {ex.Message}" );

                            goto skipVersion;
                        }
                    }
                    catch ( Exception e )
                    {
                        context.Console.WriteError( $"Failed to fetch package information for {packageName} version {version}. Exception: {e.Message}" );

                        goto skipVersion;
                    }
                }

                packageInfoMap[packageName] = packageInfo;
            }

            if ( packageVersionInfo != null )
            {
                ApplyPackageOverrides( context, packageName, packageVersionInfo, ref usageKind );

                packageVersionInfo!.Usage.Add( usageKind );

                if ( usageKind != DependentPackageUsageKind.Private )
                {
                    if ( projectUsageInfo?.PublicFacingPackages == null )
                    {
                        packageVersionInfo.UsedBy.Add( dependency.ProjectName );
                    }
                    else
                    {
                        foreach ( var publicFacingPackage in projectUsageInfo.PublicFacingPackages )
                        {
                            packageVersionInfo.UsedBy.Add( publicFacingPackage );
                        }
                    }
                }
            }

        skipVersion: ;
        }

        return packageInfoMap.Values;
    }

    private static void ApplyPackageOverrides( BuildContext context, string packageName, PackageVersionInfo packageInfo, ref DependentPackageUsageKind usage )
    {
        var packageOverrides = _defaultDependentPackageInfoOverrides.Concat( context.Product.DependentPackageInfoOverrides )
            .Where( o => packageName.StartsWith( o.Name, StringComparison.OrdinalIgnoreCase ) )
            .OrderBy( o => o.Name.Length );

        foreach ( var packageOverride in packageOverrides )
        {
            if ( packageOverride.License != null )
            {
                packageInfo.License = packageOverride.License;
            }

            if ( packageOverride.RepositoryUrl != null )
            {
                packageInfo.SourceRepository = packageOverride.RepositoryUrl;
            }

            if ( packageOverride.UsageKind != null )
            {
                usage = packageOverride.UsageKind.Value;
            }
        }
    }

    public static async Task<IReadOnlyCollection<PackageInfo>> FindDependenciesAsync( BuildContext context )
    {
        var dependencies = GetPackageDependencies( context );

        var packages = await GetPackageInfoAsync( context, dependencies );

        return packages;
    }
}