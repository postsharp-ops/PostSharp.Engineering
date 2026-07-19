// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Utilities;

internal class FileDownloader : IDisposable
{
    /// <summary>
    /// How long a transfer may receive no data at all before it is given up on. This is an idle timeout, not a
    /// total-duration one: every read that returns data pushes it forward, so an arbitrarily large file transfers
    /// successfully over an arbitrarily slow link, while a connection that stops delivering bytes still fails
    /// promptly instead of hanging.
    /// </summary>
    public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds( 60 );

    private const int _maxRetries = 3;

    private readonly SemaphoreSlim _throttler = new( 4, 4 );
    private readonly IEnumerable<DownloadedFile> _files;
    private readonly HttpClient _httpClient;
    private readonly ProgressContext? _progressContext;
    private readonly ConsoleHelper _console;
    private readonly StringTrimmer _descriptionTrimmer;
    private readonly CancellationToken _cancellationToken;
    private readonly TimeSpan _stallTimeout;

    private bool _used;

    /// <param name="stallTimeout">Overrides <see cref="DefaultStallTimeout"/>. Only tests pass this, so that they do
    /// not have to wait out the production window.</param>
    public static Task<bool> DownloadAsync(
        IEnumerable<DownloadedFile> files,
        HttpClient httpClient,
        ConsoleHelper console,
        bool showProgress,
        TimeSpan? stallTimeout = null )
    {
        var cancellationToken = ConsoleHelper.CancellationToken;
        var effectiveStallTimeout = stallTimeout ?? DefaultStallTimeout;

        if ( showProgress )
        {
            return Task.Run(
                () => AnsiConsole.Progress()
                    .Columns(
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new DownloadedColumn(),
                        new TransferSpeedColumn(),
                        new RemainingTimeColumn(),
                        new ElapsedTimeColumn(),
                        new SpinnerColumn() )
                    .StartAsync( ctx => new FileDownloader( files, httpClient, ctx, console, cancellationToken, effectiveStallTimeout ).DownloadAsync() ),
                cancellationToken );
        }
        else
        {
            return new FileDownloader( files, httpClient, null, console, cancellationToken, effectiveStallTimeout ).DownloadAsync();
        }
    }

    private FileDownloader(
        IEnumerable<DownloadedFile> files,
        HttpClient httpClient,
        ProgressContext? progressContext,
        ConsoleHelper console,
        CancellationToken cancellationToken,
        TimeSpan stallTimeout )
    {
        this._files = files;
        this._httpClient = httpClient;
        this._progressContext = progressContext;
        this._console = console;
        this._descriptionTrimmer = new StringTrimmer( this._console.ConsoleWidth / 2 );
        this._cancellationToken = cancellationToken;
        this._stallTimeout = stallTimeout;
    }

    private async Task<(bool Result, DownloadedFile File, Exception? Exception)> DownloadFileAsync(
        ProgressTask? progress,
        DownloadedFile file )
    {
        var attempt = 0;

        try
        {
            while ( true )
            {
                // Tracks whether this iteration actually took a permit. Both the retry delay and the wait itself
                // observe the cancellation token, and releasing a permit that was never acquired would throw
                // SemaphoreFullException out of the finally block, faulting this task and, through the rethrow in
                // DownloadAsync, abandoning every download still in flight.
                var hasThrottlerPermit = false;

                try
                {
                    if ( attempt > 0 )
                    {
                        ((IProgress<double>?) progress)?.Report( 0 );

                        var delay = Math.Pow( 2, attempt );

                        for ( var i = delay; i > 0; i-- )
                        {
                            progress?.Description( this._descriptionTrimmer.Trim( $"{file.Description} failed, retrying in {i} seconds" ) );

                            await Task.Delay( TimeSpan.FromSeconds( 1 ), this._cancellationToken );
                        }
                    }

                    await this._throttler.WaitAsync( this._cancellationToken );
                    hasThrottlerPermit = true;

                    if ( attempt == 0 )
                    {
                        progress?.StartTask();
                    }

                    await this.DownloadOnceAsync( progress, file );

                    return (true, file, null);
                }
                catch ( Exception e )
                {
                    if ( attempt < _maxRetries && !this._cancellationToken.IsCancellationRequested )
                    {
                        attempt++;

                        continue;
                    }

                    progress?.Description( this._descriptionTrimmer.Trim( $"{file.Description} failed: {e.Message}" ) );

                    return (false, file, e);
                }
                finally
                {
                    if ( hasThrottlerPermit )
                    {
                        this._throttler.Release();
                    }
                }
            }
        }
        finally
        {
            progress?.StopTask();
        }
    }

    /// <summary>
    /// Performs a single download attempt, guarded by an idle timeout rather than by a total-duration one.
    /// </summary>
    private async Task DownloadOnceAsync( ProgressTask? progress, DownloadedFile file )
    {
        // Linked to the outer token so that Ctrl-C still cancels, and pushed forward by every read that returns
        // data. This watchdog is the only thing bounding the body transfer: HttpClient.Timeout stops applying once
        // the response headers have arrived, so before this existed a server that went silent mid-body hung the
        // download forever -- no exception, no message, no progress.
        using var stallWatchdog = CancellationTokenSource.CreateLinkedTokenSource( this._cancellationToken );
        stallWatchdog.CancelAfter( this._stallTimeout );

        try
        {
            // One request, not two, and disposed. The previous code issued a second GET just to obtain the stream
            // and abandoned this first response undisposed, so the server kept streaming that first body over an
            // already constrained link while the second request waited for its headers -- and the header phase is
            // exactly what HttpClient.Timeout does bound, which is how the largest artifacts hit the 100 s limit.
            // Presigned S3 URLs also carry X-Amz-Expires, so the second request could be issued against a URL that
            // had expired in the meantime.
            using var response = await this._httpClient.GetAsync(
                file.SourceUrl,
                HttpCompletionOption.ResponseHeadersRead,
                stallWatchdog.Token );

            if ( !response.IsSuccessStatusCode )
            {
                throw new IOException( $"{response.StatusCode} {response.ReasonPhrase}" );
            }

            var directory = Path.GetDirectoryName( file.TargetFile )
                            ?? throw new InvalidOperationException( $"Directory of '{file.TargetFile}' could not be determined." );

            Directory.CreateDirectory( directory );

            await using var httpStream = await response.Content.ReadAsStreamAsync( stallWatchdog.Token );
            await using var fileStream = File.Open( file.TargetFile, FileMode.Create, FileAccess.Write, FileShare.None );

            var buffer = new byte[4096];
            int bytesRead;

            while ( (bytesRead = await httpStream.ReadAsync( buffer, stallWatchdog.Token )) != 0 )
            {
                // Data arrived, so restart the idle window before doing anything else with it.
                stallWatchdog.CancelAfter( this._stallTimeout );

                await fileStream.WriteAsync( buffer.AsMemory( 0, bytesRead ), stallWatchdog.Token );
                progress?.Increment( bytesRead );
            }
        }
        catch ( OperationCanceledException ) when ( !this._cancellationToken.IsCancellationRequested )
        {
            // The user did not cancel, so only the watchdog can have fired. Translated into a distinct exception so
            // that the reported message names the real cause, and so that the caller retries this the way it
            // retries any other transport failure instead of treating it as the user's Ctrl-C, which must not be
            // retried.
            throw new TimeoutException( $"no data received for {this._stallTimeout.TotalSeconds:F0} seconds" );
        }
    }

    private async Task<bool> DownloadAsync()
    {
        if ( this._used )
        {
            throw new InvalidOperationException( "The FileDownloader can be used only once." );
        }

        this._used = true;

        var pendingDownloads = new List<Task<(bool Result, DownloadedFile File, Exception? Exception)>>();

        foreach ( var file in this._files )
        {
            var progress = this._progressContext?.AddTask( this._descriptionTrimmer.Trim( file.Description ), false, file.Length );
            var task = this.DownloadFileAsync( progress, file );

            pendingDownloads.Add( task );
        }

        List<(DownloadedFile File, Exception? Exception)> failedDownloads = new();

        while ( pendingDownloads.Any() )
        {
            Task<(bool Succeeded, DownloadedFile File, Exception? Exception)> completedDownloadTask = await Task.WhenAny( pendingDownloads );
            pendingDownloads.Remove( completedDownloadTask );
            var completedDownload = await completedDownloadTask;

            if ( !completedDownload.Succeeded && !this._cancellationToken.IsCancellationRequested )
            {
                failedDownloads.Add( (completedDownload.File, completedDownload.Exception) );
            }
        }

        this._throttler.Dispose();

        failedDownloads.ForEach( d => this._console.WriteError( $"{d.File.Description} failed to download: {d.Exception?.Message ?? "Unknown reason"}" ) );

        return !failedDownloads.Any();
    }

    public void Dispose() => this._throttler.Dispose();
}