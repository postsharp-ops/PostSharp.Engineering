// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Newtonsoft.Json;
using PostSharp.Engineering.BuildTools.Utilities;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;

namespace PostSharp.Engineering.BuildTools.NuGet;

[UsedImplicitly]
public class UnlistNugetPackageCommand : Command<UnlistNugetPackageCommandSettings>
{
    public override int Execute( CommandContext context, UnlistNugetPackageCommandSettings settings )
        => Execute( new ConsoleHelper(), settings ) ? 0 : 1;

    private static bool Execute( ConsoleHelper console, UnlistNugetPackageCommandSettings settings )
    {
        if ( string.IsNullOrEmpty( settings.PackageName ) )
        {
            console.WriteError( "No package specified for unlisting from NuGet." );

            return false;
        }

        var packageName = settings.PackageName.ToLowerInvariant();

        console.WriteMessage( $"Retrieving all versions of package '{packageName}'." );

        if ( !TryGetAllPackageVersions( console, packageName, out var versions ) )
        {
            console.WriteError( $"Failed to get all versions of '{packageName}' package." );

            return false;
        }

        if ( !TryDeserializeVersions( versions, out var packageVersions ) )
        {
            console.WriteError( $"Failed to deserialize versions of '{packageName}' package." );

            return false;
        }

        console.WriteMessage( $"Unlisting all versions of package '{packageName}'." );

        if ( !UnlistPackage( console, packageName, packageVersions ) )
        {
            console.WriteError( $"Failed to unlist all package versions of '{packageName}'." );

            return false;
        }

        console.WriteSuccess( $"Successfully unlisted all versions of '{packageName}' package." );

        return true;
    }

    private static bool TryGetAllPackageVersions( ConsoleHelper console, string packageName, [NotNullWhen( true )] out string? packageVersionsJson )
    {
        var httpClient = new HttpClient();

        string? versionsJson;

        try
        {
            versionsJson = httpClient.GetStringAsync( $"https://api.nuget.org/v3-flatcontainer/{packageName}/index.json" ).Result;
        }
        catch ( Exception e )
        {
            console.WriteError( e.Message );

            packageVersionsJson = null;

            return false;
        }

        if ( string.IsNullOrEmpty( versionsJson ) )
        {
            packageVersionsJson = null;

            return false;
        }

        packageVersionsJson = versionsJson;

        return true;
    }

    private static bool TryDeserializeVersions( string versionsJson, [NotNullWhen( true )] out List<string>? packageVersions )
    {
        var deserializedVersions = JsonConvert.DeserializeObject<Versions>( versionsJson );

        if ( deserializedVersions == null || deserializedVersions.VersionsList == null )
        {
            packageVersions = null;

            return false;
        }

        packageVersions = deserializedVersions.VersionsList;

        return true;
    }

    private static bool UnlistPackage( ConsoleHelper console, string packageName, List<string> packageVersions )
    {
        var nugetUnlistApiKeyEnvironmentVariable = "NUGET_ORG_UNLIST_API_KEY";
        var nugetUnlistApiKey = Environment.GetEnvironmentVariable( nugetUnlistApiKeyEnvironmentVariable );

        if ( string.IsNullOrEmpty( nugetUnlistApiKey ) )
        {
            console.WriteImportantMessage(
                $"'{nugetUnlistApiKeyEnvironmentVariable}' environment variable required for unlisting the package from NuGet.org is not set." );

            return false;
        }

        var nugetServerUrlEnvironmentVariable = "NUGET_ORG_GET_URL";
        var nugetServerUrl = Environment.GetEnvironmentVariable( nugetServerUrlEnvironmentVariable );

        if ( string.IsNullOrEmpty( nugetServerUrl ) )
        {
            console.WriteImportantMessage(
                $"'{nugetServerUrlEnvironmentVariable}' environment variable not set. Set it to a supported server URL, e.g. 'https://www.nuget.org'." );

            return false;
        }

        var success = true;

        foreach ( var version in packageVersions )
        {
            if ( !ToolInvocationHelper.InvokeTool(
                    console,
                    "dotnet",
                    $"nuget delete {packageName} {version} --api-key {nugetUnlistApiKey} --non-interactive --source {nugetServerUrl}",
                    Directory.GetCurrentDirectory() ) )
            {
                success = false;

                // We don't want to repeat the same error for all versions.
                break;
            }
        }

        return success;
    }

    internal class Versions
    {
        [JsonProperty( "versions" )]
        public List<string>? VersionsList { get; set; }
    }
}