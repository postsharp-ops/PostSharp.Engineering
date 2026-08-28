// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Files;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;
using System.Xml.XPath;

namespace PostSharp.Engineering.BuildTools.Dependencies;

[UsedImplicitly]
internal class UpdateEngineeringCommand : BaseCommand<UpdateEngineeringCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, UpdateEngineeringCommandSettings settings )
    {
        if ( settings.Retry )
        {
            do
            {
                var exitCode = this.ExecuteOnce( context, settings );

                if ( exitCode == ExitCode.Success )
                {
                    return true;
                }

                context.Console.WriteMessage( "Waiting for 30 seconds before retrying." );
                Thread.Sleep( TimeSpan.FromSeconds( 30 ) );
            }
            while ( true );
        }
        else
        {
            context.ExitCode = this.ExecuteOnce( context, settings );

            return context.ExitCode == ExitCode.Success;
        }
    }

    private ExitCode ExecuteOnce( BuildContext context, UpdateEngineeringCommandSettings settings )
    {
        var httpClient = new HttpClient();

        var nugetResponse = httpClient.GetAsync( "https://azuresearch-usnc.nuget.org/query?q=PostSharp.Engineering.Sdk&prerelease=true&semVerLevel=2.0.0" )
            .Result;

        var jsonText = nugetResponse.Content.ReadAsStringAsync().Result;
        var json = JsonDocument.Parse( jsonText );
        var currentVersion = this.GetType().Assembly.GetName().Version!;
        var majorVersion = currentVersion.ToString( 2 );

        var versions = json.RootElement.GetProperty( "data" )
            .EnumerateArray()
            .SelectMany( i => i.GetProperty( "versions" ).EnumerateArray() )
            .Select( v => v.GetProperty( "version" ).GetString()! )
            .Where( v => v.StartsWith( majorVersion, StringComparison.Ordinal ) )
            .ToList();

        var lastVersion = versions.Last();

        var product = context.Product;
        var console = context.Console;
        var madeAnyChange = false;

        if ( !product.GenerateNuGetConfig )
        {
            // Update global.json.
            console.WriteImportantMessage( $"Updating engineering to version {lastVersion}." );

            // Update all global.jsons in the repo. (This is the case for Metalama.Try, for example.)
            var globalJsonName = "global.json";
            var globalJsonPaths = Directory.EnumerateFiles( context.RepoDirectory, globalJsonName, SearchOption.AllDirectories );

            foreach ( var globalJsonPath in globalJsonPaths )
            {
                var globalJsonRelativePath = Path.GetRelativePath( context.RepoDirectory, globalJsonPath );

                // Skip files contained in other repositories, eg. those in source-dependencies.
                if ( globalJsonRelativePath != globalJsonName )
                {
                    var globalJsonPathParts = globalJsonRelativePath.Split( Path.DirectorySeparatorChar );

                    if ( Directory.EnumerateDirectories(
                            Path.Combine( context.RepoDirectory, globalJsonPathParts[0] ),
                            ".git",
                            SearchOption.AllDirectories )
                        .Any() )
                    {
                        console.WriteWarning( $"File '{globalJsonPath}' not updated because it is contained in another repository." );

                        continue;
                    }
                }

                var globalJson = JObject.Parse( File.ReadAllText( globalJsonPath ) );
                var globalJsonProperty = globalJson["msbuild-sdks"]?["PostSharp.Engineering.Sdk"];

                if ( globalJsonProperty != null )
                {
                    if ( globalJsonProperty.Value<string>()?.Trim() != lastVersion )
                    {
                        madeAnyChange = true;

                        console.WriteMessage( $"Writing '{globalJsonPath}'." );

                        globalJsonProperty.Replace( new JValue( lastVersion ) );
                        using var writer = new StreamWriter( globalJsonPath );
                        var jsonTextWriter = new JsonTextWriter( writer ) { Formatting = Formatting.Indented };

                        globalJson.WriteTo( jsonTextWriter );
                    }
                }
                else
                {
                    console.WriteWarning( $"File '{globalJsonPath}' not updated because there is no reference to PostSharp.Engineering.Sdk." );
                }
            }
        }
        else
        {
            console.WriteMessage( "File 'global.json' not updated because it is auto-generated." );
        }

        // Update Directory.Packages.props or Versions.props
        var centralPackageManagementVersionsPath = Path.Combine( context.RepoDirectory, "Directory.Packages.props" );

        var versionsFilePath = File.Exists( centralPackageManagementVersionsPath )
            ? centralPackageManagementVersionsPath
            : Path.Combine( context.RepoDirectory, context.Product.VersionsFilePath );

        var versionsFile = XDocument.Load( versionsFilePath, LoadOptions.PreserveWhitespace );
        var versionProperties = versionsFile.XPathSelectElements( "/Project/PropertyGroup/PostSharpEngineeringVersion" ).ToList();

        if ( versionProperties.Count == 1 )
        {
            if ( versionProperties[0].Value != lastVersion )
            {
                console.WriteMessage( $"Writing '{versionsFilePath}'." );
                versionProperties[0].Value = lastVersion;
                versionsFile.Save( versionsFilePath );
                madeAnyChange = true;
            }
        }
        else
        {
            console.WriteWarning(
                $"File '{versionsFilePath}' not updated because there are {versionProperties.Count} properties named PostSharpEngineeringVersion." );
        }

        if ( madeAnyChange )
        {
            RefreshGeneratedDependencyFiles( context, settings );

            console.WriteSuccess( $"PostSharp.Engineering successfully updated to version {lastVersion}." );

            // Generate scripts.
            console.WriteWarning( "Now run `./Build.ps1 generate-scripts` with this new version." );

            return ExitCode.Success;
        }
        else
        {
            console.WriteWarning( $"PostSharp.Engineering was already of the latest version ({lastVersion})." );

            return ExitCode.NoChangeMade;
        }
    }

    /// <summary>
    /// Rewrites the already-generated <c>Versions.{Configuration}.g.props</c> files so that they pick up the version we have just
    /// written to <c>global.json</c> and <c>Directory.Packages.props</c>.
    /// </summary>
    /// <remarks>
    /// Without this, the update would appear to have no effect. Those files assign <c>PostSharpEngineeringVersion</c> unconditionally
    /// and are imported before <c>Directory.Packages.props</c>, so as long as they hold the previous version, the next build of the
    /// product definition restores the previous package and keeps running the previous version of this tool.
    /// </remarks>
    private static void RefreshGeneratedDependencyFiles( BuildContext context, UpdateEngineeringCommandSettings settings )
    {
        var console = context.Console;

        foreach ( var configuration in Enum.GetValues<BuildConfiguration>() )
        {
            var path = DependenciesConfigurationFile.GetPath( context, settings, configuration );

            if ( !File.Exists( path ) )
            {
                continue;
            }

            // A failure here is not worth failing the update for: the version has already been written to the files that source
            // control tracks, and 'prepare' regenerates these ones anyway.
            try
            {
                if ( !DependenciesConfigurationFile.TryLoad( context, settings, configuration, out var dependenciesConfigurationFile )
                     || !dependenciesConfigurationFile.TryWrite( context ) )
                {
                    console.WriteWarning( $"Could not refresh '{path}'. Run './Build.ps1 prepare' to update it." );
                }
            }
            catch ( Exception e )
            {
                console.WriteWarning( $"Could not refresh '{path}': {e.Message} Run './Build.ps1 prepare' to update it." );
            }
        }
    }
}