// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// A test-only NuGet v3 feed, served over HTTP on the loopback interface from the nupkg files of a directory. No build and no
/// product uses it.
/// </summary>
/// <remarks>
/// <para>
/// <c>TestArchivesTests</c> uses this feed to test the packages of a feed other than nuget.org without network access. A
/// restore from this feed records an HTTP source for the package, as a restore from a mirror such as
/// <c>roslyn-consolidated</c> does. The test then verifies that the manifest of the test archive names the feed, and that
/// <c>RunTests.ps1</c> downloads the package from it.
/// </para>
/// <para>
/// The feed implements only what a restore and <c>RunTests.ps1</c> request: the service index, and the package base address
/// resource, which lists the versions of a package and serves its nupkg.
/// </para>
/// </remarks>
internal sealed class LocalNuGetFeed : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _directory;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _loop;

    public LocalNuGetFeed( string directory )
    {
        this._directory = directory;

        // Port 0 is not available to HttpListener, so probe for a free port instead.
        for ( var port = 18900; port < 19000; port++ )
        {
            var listener = new HttpListener();
            listener.Prefixes.Add( $"http://127.0.0.1:{port}/" );

            try
            {
                listener.Start();
                this._listener = listener;
                this.Prefix = $"http://127.0.0.1:{port}/";
                this._loop = Task.Run( this.ServeAsync );

                return;
            }
            catch ( HttpListenerException )
            {
                listener.Close();
            }
        }

        throw new InvalidOperationException( "Could not find a free port for the test feed." );
    }

    /// <summary>
    /// Gets the base URL of the server, with a trailing slash.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// Gets the URL of the service index, which is the value of the source in <c>nuget.config</c>.
    /// </summary>
    public string ServiceIndex => this.Prefix + "v3/index.json";

    /// <summary>
    /// Gets the paths of the nupkg files that the feed served, in lower case.
    /// </summary>
    public ConcurrentBag<string> DownloadedPackages { get; } = [];

    private async Task ServeAsync()
    {
        while ( !this._cancellation.IsCancellationRequested )
        {
            HttpListenerContext context;

            try
            {
                context = await this._listener.GetContextAsync().WaitAsync( this._cancellation.Token );
            }
            catch ( Exception e ) when ( e is OperationCanceledException or HttpListenerException or ObjectDisposedException )
            {
                return;
            }

            // A restore sends several requests at a time.
            _ = Task.Run( () => this.Respond( context ) );
        }
    }

    private void Respond( HttpListenerContext context )
    {
        using var response = context.Response;
        var path = context.Request.Url!.AbsolutePath.ToLowerInvariant();

        try
        {
            if ( path == "/v3/index.json" )
            {
                // ProGet writes the address with a leading space, which the runner must accept as NuGet does.
                WriteJson(
                    response,
                    new Resources( "3.0.0", [new Resource( " " + this.Prefix + "flat/", "PackageBaseAddress/3.0.0" )] ) );

                return;
            }

            // /flat/<id>/index.json lists the versions of a package, and /flat/<id>/<version>/<id>.<version>.nupkg is the package.
            var parts = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );

            if ( parts.Length == 3 && parts[0] == "flat" && parts[2] == "index.json" )
            {
                var versions = Directory.GetFiles( this._directory, "*.nupkg" )
                    .Select( f => Path.GetFileNameWithoutExtension( f ).ToLowerInvariant() )
                    .Where( n => n.StartsWith( parts[1] + ".", StringComparison.Ordinal ) && char.IsDigit( n[parts[1].Length + 1] ) )
                    .Select( n => n[(parts[1].Length + 1)..] )
                    .ToArray();

                if ( versions.Length == 0 )
                {
                    response.StatusCode = 404;

                    return;
                }

                WriteJson( response, new Versions( versions ) );

                return;
            }

            if ( parts.Length == 4 && parts[0] == "flat" )
            {
                var file = Directory.GetFiles( this._directory, "*.nupkg" )
                    .FirstOrDefault( f => Path.GetFileName( f ).Equals( parts[3], StringComparison.OrdinalIgnoreCase ) );

                if ( file == null )
                {
                    response.StatusCode = 404;

                    return;
                }

                this.DownloadedPackages.Add( path );
                response.ContentType = "application/octet-stream";
                var bytes = File.ReadAllBytes( file );
                response.OutputStream.Write( bytes );

                return;
            }

            response.StatusCode = 404;
        }
        catch ( HttpListenerException )
        {
            // The client closed the connection.
        }
    }

    private static void WriteJson<T>( HttpListenerResponse response, T value )
    {
        response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes( JsonSerializer.Serialize( value ) );
        response.OutputStream.Write( bytes );
    }

    public void Dispose()
    {
        this._cancellation.Cancel();
        this._listener.Close();

        try
        {
            this._loop.Wait( TimeSpan.FromSeconds( 10 ) );
        }
        catch ( AggregateException )
        {
            // The loop ends with the listener.
        }

        this._cancellation.Dispose();
    }

    private sealed record Resources(
        [property: JsonPropertyName( "version" )]
        string Version,
        [property: JsonPropertyName( "resources" )]
        Resource[] Items );

    private sealed record Resource(
        [property: JsonPropertyName( "@id" )]
        string Id,
        [property: JsonPropertyName( "@type" )]
        string Type );

    private sealed record Versions( [property: JsonPropertyName( "versions" )] string[] Items );
}
