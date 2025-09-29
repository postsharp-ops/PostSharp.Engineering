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
    private readonly SemaphoreSlim _throttler = new( 4, 4 );
    private readonly IEnumerable<DownloadedFile> _files;
    private readonly HttpClient _httpClient;
    private readonly ProgressContext? _progressContext;
    private readonly ConsoleHelper _console;
    private readonly StringTrimmer _descriptionTrimmer;
    private readonly CancellationToken _cancellationToken;

    private bool _used;

    public static Task<bool> DownloadAsync( IEnumerable<DownloadedFile> files, HttpClient httpClient, ConsoleHelper console, bool showProgress )
    {
        var cancellationToken = ConsoleHelper.CancellationToken;

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
                    .StartAsync( ctx => new FileDownloader( files, httpClient, ctx, console, cancellationToken ).DownloadAsync() ),
                cancellationToken );
        }
        else
        {
            return new FileDownloader( files, httpClient, null, console, cancellationToken ).DownloadAsync();
        }
    }

    private FileDownloader(
        IEnumerable<DownloadedFile> files,
        HttpClient httpClient,
        ProgressContext? progressContext,
        ConsoleHelper console,
        CancellationToken cancellationToken )
    {
        this._files = files;
        this._httpClient = httpClient;
        this._progressContext = progressContext;
        this._console = console;
        this._descriptionTrimmer = new StringTrimmer( this._console.ConsoleWidth / 2 );
        this._cancellationToken = cancellationToken;
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

                    if ( attempt == 0 )
                    {
                        progress?.StartTask();
                    }

                    var response = await this._httpClient.GetAsync( file.SourceUrl, HttpCompletionOption.ResponseHeadersRead, this._cancellationToken );

                    if ( !response.IsSuccessStatusCode )
                    {
                        throw new IOException( $"{response.StatusCode} {response.ReasonPhrase}" );
                    }

                    var directory = Path.GetDirectoryName( file.TargetFile )
                                    ?? throw new InvalidOperationException( $"Directory of '{file.TargetFile}' could not be determined." );

                    Directory.CreateDirectory( directory );

                    await using var httpStream = await this._httpClient.GetStreamAsync( file.SourceUrl, this._cancellationToken );
                    await using var fileStream = File.Open( file.TargetFile, FileMode.Create, FileAccess.Write, FileShare.None );

                    var buffer = new byte[4096];
                    int bytesRead;

                    while ( (bytesRead = await httpStream.ReadAsync( buffer, 0, buffer.Length, this._cancellationToken )) != 0 )
                    {
                        await fileStream.WriteAsync( buffer, 0, bytesRead, this._cancellationToken );
                        progress?.Increment( bytesRead );
                    }

                    return (true, file, null);
                }
                catch ( Exception e )
                {
                    if ( attempt < 3 && !this._cancellationToken.IsCancellationRequested )
                    {
                        attempt++;

                        continue;
                    }

                    progress?.Description( this._descriptionTrimmer.Trim( $"{file.Description} failed: {e.Message}" ) );

                    return (false, file, e);
                }
                finally
                {
                    this._throttler.Release();
                }
            }
        }
        finally
        {
            progress.StopTask();
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