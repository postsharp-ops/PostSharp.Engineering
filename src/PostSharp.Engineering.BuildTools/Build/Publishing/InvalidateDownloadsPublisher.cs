// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Net;
using System.Net.Http;

namespace PostSharp.Engineering.BuildTools.Build.Publishing;

public class HttpGetPublisher : Publisher
{
    public string Url { get; }

    public HttpGetPublisher( string url )
    {
        this.Url = url;
    }

    protected override bool Publish(
        BuildContext context,
        PublishSettings settings,
        (string Private, string Public) directories,
        BuildConfigurationInfo configuration,
        BuildArguments buildArguments,
        bool isPublic,
        ref bool hasTarget )
    {
        if ( settings.Dry )
        {
            return true;
        }

        try
        {
            var url = Environment.ExpandEnvironmentVariables( this.Url );
            using var httpClient = new HttpClient();
            var invalidationResponse = httpClient.GetAsync( url ).GetAwaiter().GetResult();

            if ( invalidationResponse.StatusCode != HttpStatusCode.OK )
            {
                context.Console.WriteError(
                    $"Failed to invalidate {this.Url}: {invalidationResponse.StatusCode} {invalidationResponse.ReasonPhrase} / {invalidationResponse.Content.ReadAsString()}" );

                return false;
            }
        }
        catch ( Exception e )
        {
            context.Console.WriteError( $"Failed to invalidate {this.Url}: {e.Message}" );

            return false;
        }

        return true;
    }
}