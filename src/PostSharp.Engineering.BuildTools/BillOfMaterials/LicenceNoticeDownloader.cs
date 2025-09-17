// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.BillOfMaterials;

public static class LicenceNoticeDownloader
{
    public static async Task AppendLicenseAndNoticeFilesAsync(
        BuildContext context,
        string repoUrl,
        IReadOnlyCollection<string> consumedPackages,
        IReadOnlyCollection<string> consumingPackages,
        TextWriter textWriter )
    {
        var httpClientHandler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };

        var gitHubToken = Environment.GetEnvironmentVariable( EnvironmentVariableNames.GitHubToken );

        if ( string.IsNullOrEmpty( gitHubToken ) )
        {
            throw new InvalidOperationException( "The GITHUB_TOKEN environment variable must be set." );
        }

        using var gitHubHttpClient = new HttpClient( httpClientHandler );
        gitHubHttpClient.DefaultRequestHeaders.UserAgent.Add( new ProductInfoHeaderValue( typeof(LicenceNoticeDownloader).Assembly.GetName().Name!, "1.0" ) );
        gitHubHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", gitHubToken );

        if ( !Uri.TryCreate( repoUrl, UriKind.Absolute, out var uri ) || uri.Host != "github.com" )
        {
            context.Console.WriteError( $"Invalid or non-GitHub repository URL: {repoUrl}" );

            return;
        }

        var segments = uri.AbsolutePath.Trim( '/' ).Split( '/' );

        if ( segments.Length < 2 )
        {
            context.Console.WriteError( $"Invalid GitHub repository URL: {repoUrl}" );

            return;
        }

        var org = segments[0];
        var project = segments[1];
        var apiUrl = $"https://api.github.com/repos/{org}/{project}/contents/";

        Console.WriteLine( $"Fetching file list from {apiUrl}..." );

        for ( var retryCount = 0; retryCount < 3; retryCount++ )
        {
            try
            {
                var response = await gitHubHttpClient.GetStringAsync( apiUrl );
                var filesJson = JsonNode.Parse( response );

                if ( filesJson == null )
                {
                    context.Console.WriteError( $"Empty response for '{apiUrl}'." );

                    return;
                }

                var targetFiles = new[] { "LICENSE", "LICENSE.MD", "LICENSE.TXT", "NOTICE", "NOTICE.MD", "NOTICE.TXT" };

                var matchingFiles = filesJson.AsArray()
                    .Where( x => x != null )
                    .Select( x => (Name: x!["name"]!.ToString(), Url: x["download_url"]?.ToString()) )
                    .Where( file => targetFiles.Contains( file.Name.ToString(), StringComparer.OrdinalIgnoreCase ) )
                    .ToList();

                if ( matchingFiles.Any() )
                {
                    await textWriter.WriteLineAsync( "\n\n---\n\n" );
                    await textWriter.WriteLineAsync( $"## License notices for {project}\n" );
                    await textWriter.WriteLineAsync();

                    await textWriter.WriteLineAsync(
                        $"The following packages are consumed from this project: {string.Join( ", ", consumedPackages.Select( x => $"`{x}`" ) )}." );

                    await textWriter.WriteLineAsync();

                    await textWriter.WriteLineAsync(
                        $"This project is used by the following of our packages: {string.Join( ", ", consumingPackages.Select( x => $"`{x}`" ) )}." );

                    await textWriter.WriteLineAsync();

                    foreach ( var file in matchingFiles )
                    {
                        Console.WriteLine( $"Fetching {file.Name}..." );
                        var fileContent = await gitHubHttpClient.GetStringAsync( file.Url );

                        if ( file.Name.EndsWith( ".MD", StringComparison.OrdinalIgnoreCase ) )
                        {
                            fileContent = FormatMarkdownTitles( fileContent );
                        }

                        fileContent = UnindentContent( fileContent );
                        var quotedContent = string.Join( "\n", fileContent.Split( '\n' ).Select( line => $"> {line}" ) );

                        await textWriter.WriteLineAsync( quotedContent );
                        Console.WriteLine( $"Appended: {file.Name}" );
                    }
                }
                else
                {
                    Console.WriteLine( $"No license or notice files found for {project}." );
                }

                return;
            }
            catch ( HttpRequestException ex ) when ( ex.StatusCode == HttpStatusCode.Forbidden )
            {
                Console.WriteLine( $"HTTP 403 encountered while fetching {apiUrl}: {ex}. Retrying in 300 seconds... ({retryCount + 1}/3)." );
                await Task.Delay( 300000 );
            }
            catch ( Exception ex )
            {
                Console.WriteLine( $"Failed to fetch license and notice files for {repoUrl}. Exception: {ex.Message}" );

                break;
            }
        }
    }

    private static string FormatMarkdownTitles( string content )
    {
        return Regex.Replace( content, @"^(#+)", "###" );
    }

    private static string UnindentContent( string content )
    {
        var lines = content.Split( '\n' );

        var minIndent = lines.Where( line => !string.IsNullOrWhiteSpace( line ) )
            .Select( line => line.TakeWhile( char.IsWhiteSpace ).Count() )
            .DefaultIfEmpty( 0 )
            .Min();

        return string.Join( "\n", lines.Select( line => line.Length >= minIndent ? line[minIndent..] : line ).Select( line => line.TrimEnd() ) );
    }
}