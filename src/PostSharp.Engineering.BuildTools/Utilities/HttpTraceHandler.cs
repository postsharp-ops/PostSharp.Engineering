// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Utilities;

/// <summary>
/// Traces every HTTP request and response to the console. This is added to the pipeline only in verbose mode, where
/// the download progress bar is suppressed, because the live display of the progress bar would overwrite these lines.
/// </summary>
internal sealed class HttpTraceHandler : DelegatingHandler
{
    /// <summary>
    /// Query string parameters whose value is a credential and must never reach the console. Presigned storage URLs,
    /// which is what the build server redirects artifact requests to, carry the signature in the query string.
    /// </summary>
    private static readonly string[] _secretQueryParameters = ["X-Amz-Signature", "X-Amz-Credential", "X-Amz-Security-Token", "Signature", "AWSAccessKeyId"];

    private static readonly Regex _secretQueryParameterRegex = new(
        $"(?<name>{string.Join( "|", _secretQueryParameters.Select( Regex.Escape ) )})=(?<value>[^&]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant );

    private static int _nextRequestId;

    private readonly ConsoleHelper _console;

    public HttpTraceHandler( ConsoleHelper console, HttpMessageHandler innerHandler ) : base( innerHandler )
    {
        this._console = console;
    }

    protected override async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
    {
        // Concurrent downloads interleave their trace lines, so each exchange is numbered to be readable.
        var id = Interlocked.Increment( ref _nextRequestId );
        var stopwatch = Stopwatch.StartNew();

        this._console.WriteMessage( FormattableString.Invariant( $"HTTP #{id} > {request.Method} {Redact( request.RequestUri )}" ) );

        try
        {
            var response = await base.SendAsync( request, cancellationToken );

            // Redirects are followed inside the inner handler, so this reports the final response only. When the
            // request ends up somewhere else -- an artifact request redirected to presigned storage -- the URI it
            // was finally served from is the interesting one, so it is reported when it differs.
            var finalUri = response.RequestMessage?.RequestUri;

            if ( finalUri != null && finalUri != request.RequestUri )
            {
                this._console.WriteMessage( FormattableString.Invariant( $"HTTP #{id} = redirected to {Redact( finalUri )}" ) );
            }

            // The elapsed time is the time to the response headers, not to the end of the body: an artifact is
            // requested with HttpCompletionOption.ResponseHeadersRead, so nothing of the body has been read yet and
            // Content-Length is what the server promises rather than what it delivers. FileDownloader reports what
            // actually arrives.
            this._console.WriteMessage(
                FormattableString.Invariant(
                    $"HTTP #{id} < {(int) response.StatusCode} {response.ReasonPhrase} in {stopwatch.ElapsedMilliseconds} ms [{DescribeHeaders( response )}]" ) );

            return response;
        }
        catch ( Exception e )
        {
            this._console.WriteMessage(
                FormattableString.Invariant( $"HTTP #{id} ! {e.GetType().Name} after {stopwatch.ElapsedMilliseconds} ms: {e.Message}" ) );

            throw;
        }
    }

    private static string DescribeHeaders( HttpResponseMessage response )
    {
        var parts = new List<string>();

        if ( response.Content.Headers.ContentLength != null )
        {
            parts.Add( FormattableString.Invariant( $"Content-Length: {response.Content.Headers.ContentLength}" ) );
        }

        if ( response.Content.Headers.ContentType != null )
        {
            parts.Add( $"Content-Type: {response.Content.Headers.ContentType}" );
        }

        if ( response.Headers.Location != null )
        {
            parts.Add( $"Location: {Redact( response.Headers.Location )}" );
        }

        if ( response.Headers.TryGetValues( "Server", out var server ) )
        {
            parts.Add( $"Server: {string.Join( " ", server )}" );
        }

        return parts.Count == 0 ? "no notable headers" : string.Join( ", ", parts );
    }

    private static string Redact( Uri? uri )
        => uri == null
            ? "<null>"
            : _secretQueryParameterRegex.Replace( uri.ToString(), m => $"{m.Groups["name"].Value}=<redacted>" );
}
