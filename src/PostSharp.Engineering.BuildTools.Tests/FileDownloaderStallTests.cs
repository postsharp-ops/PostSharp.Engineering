// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The downloader used to bound a transfer by <see cref="HttpClient.Timeout"/>, which budgets the whole request
/// rather than the idle time, so a large artifact over a slow link failed at a fixed wall-clock deadline however
/// healthy the connection was. It is now bounded by an idle timeout instead. These tests pin both halves of that:
/// a slow but progressing transfer must complete, and a dead one must still fail — and fail rather than hang.
/// </summary>
public sealed class FileDownloaderStallTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _prefix;
    private readonly string _directory;

    public FileDownloaderStallTests()
    {
        this._directory = Path.Combine( Path.GetTempPath(), $"downloader-{Guid.NewGuid():N}" );
        Directory.CreateDirectory( this._directory );

        // Port 0 is not available to HttpListener, so probe for a free port instead.
        for ( var port = 18800; port < 18900; port++ )
        {
            var listener = new HttpListener();
            listener.Prefixes.Add( $"http://127.0.0.1:{port}/" );

            try
            {
                listener.Start();
                this._listener = listener;
                this._prefix = $"http://127.0.0.1:{port}/";

                return;
            }
            catch ( HttpListenerException )
            {
                listener.Close();
            }
        }

        throw new InvalidOperationException( "Could not find a free port for the test server." );
    }

    /// <summary>
    /// Serves one response, writing <paramref name="chunks"/> bodies spaced by <paramref name="delay"/> and then, if
    /// <paramref name="stallForever"/>, holding the connection open without sending anything more. That last case is
    /// the one that used to hang the downloader: headers arrive, so the request itself succeeds, and the body then
    /// never completes.
    /// </summary>
    private void StartServer( int chunks, TimeSpan delay, bool stallForever )
        => _ = Task.Run(
            async () =>
            {
                try
                {
                    var context = await this._listener.GetContextAsync();
                    var response = context.Response;
                    response.StatusCode = 200;
                    response.SendChunked = true;

                    for ( var i = 0; i < chunks; i++ )
                    {
                        await Task.Delay( delay );
                        await response.OutputStream.WriteAsync( new byte[1024] );
                        await response.OutputStream.FlushAsync();
                    }

                    if ( stallForever )
                    {
                        // Never close: the client must decide on its own that nothing is coming.
                        await Task.Delay( Timeout.InfiniteTimeSpan );
                    }

                    response.Close();
                }
                catch ( Exception )
                {
                    // The listener is torn down at the end of the test; nothing to report.
                }
            } );

    private async Task<(bool Succeeded, TimeSpan Elapsed)> DownloadAsync( TimeSpan stallTimeout )
    {
        using var httpClient = new HttpClient();

        // Exactly the production setting: no total-duration budget, so only the idle timeout can end the transfer.
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        var target = Path.Combine( this._directory, "artifact.bin" );
        var file = new DownloadedFile( this._prefix + "artifact.bin", target, "artifact.bin", 0 );

        var stopwatch = Stopwatch.StartNew();

        var succeeded = await FileDownloader.DownloadAsync(
            [file],
            httpClient,
            new ConsoleHelper(),
            showProgress: false,
            stallTimeout );

        stopwatch.Stop();

        return (succeeded, stopwatch.Elapsed);
    }

    /// <summary>
    /// The regression. Ten chunks a second apart run to six seconds, well past the two-second idle window and past
    /// what a total-duration timeout of that size would have allowed, yet no single gap exceeds the window.
    /// </summary>
    [Fact]
    public async Task SlowButProgressingTransfer_Succeeds()
    {
        this.StartServer( chunks: 10, delay: TimeSpan.FromMilliseconds( 600 ), stallForever: false );

        var (succeeded, elapsed) = await this.DownloadAsync( TimeSpan.FromSeconds( 2 ) );

        Assert.True( succeeded, "A slow but progressing transfer was reported as failed." );

        Assert.True(
            elapsed > TimeSpan.FromSeconds( 2 ),
            $"The transfer finished in {elapsed.TotalSeconds:F1}s, which is too fast to prove it outlived the idle window." );

        Assert.Equal( 10 * 1024, new FileInfo( Path.Combine( this._directory, "artifact.bin" ) ).Length );
    }

    /// <summary>
    /// The hang. Headers and a first chunk arrive, then the server goes silent forever. The download must give up
    /// on its own, and must do so by the idle window times the retry count rather than by never returning.
    /// </summary>
    [Fact]
    public async Task StalledTransfer_FailsInsteadOfHanging()
    {
        this.StartServer( chunks: 1, delay: TimeSpan.Zero, stallForever: true );

        var stallTimeout = TimeSpan.FromSeconds( 2 );
        var download = this.DownloadAsync( stallTimeout );

        // Four attempts of a two-second window, plus 2+4+8s of retry backoff, is ~22s; the generous bound here is
        // only there to distinguish "gave up" from "hung forever".
        var finished = await Task.WhenAny( download, Task.Delay( TimeSpan.FromSeconds( 90 ) ) );

        Assert.True( finished == download, "The download never returned: it hung on a stalled connection." );

        var (succeeded, _) = await download;

        Assert.False( succeeded, "A stalled transfer was reported as successful." );
    }

    public void Dispose()
    {
        this._listener.Close();

        try
        {
            Directory.Delete( this._directory, true );
        }
        catch ( IOException )
        {
            // A file may still be held; the temp directory is disposable anyway.
        }
    }
}
