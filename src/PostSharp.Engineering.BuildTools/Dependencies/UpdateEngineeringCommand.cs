// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.XPath;

namespace PostSharp.Engineering.BuildTools.Dependencies;

[UsedImplicitly]
internal class UpdateEngineeringCommand : BaseCommand<CommonCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, CommonCommandSettings settings )
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
                    console.WriteMessage( $"Writing '{globalJsonPath}'." );

                    globalJsonProperty.Replace( new JValue( lastVersion ) );
                    using var writer = new StreamWriter( globalJsonPath );
                    var jsonTextWriter = new JsonTextWriter( writer ) { Formatting = Formatting.Indented };

                    globalJson.WriteTo( jsonTextWriter );
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

        console.WriteMessage( $"Writing '{versionsFilePath}'." );
        var versionsFile = XDocument.Load( versionsFilePath, LoadOptions.PreserveWhitespace );
        var versionProperties = versionsFile.XPathSelectElements( "/Project/PropertyGroup/PostSharpEngineeringVersion" ).ToList();

        if ( versionProperties.Count == 1 )
        {
            versionProperties[0].Value = lastVersion;
        }
        else
        {
            console.WriteWarning(
                $"File '{versionsFilePath}' not updated because there are {versionProperties.Count} properties named PostSharpEngineeringVersion." );
        }

        versionsFile.Save( versionsFilePath );

        console.WriteSuccess( "Engineering successfully updated." );

        // Generate scripts.
        console.WriteWarning( "Now run `./Build.ps1 generate-scripts` with this new version." );

        return true;
    }
}