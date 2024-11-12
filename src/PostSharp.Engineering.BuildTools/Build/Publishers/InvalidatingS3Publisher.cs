// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.S3.Publishers;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace PostSharp.Engineering.BuildTools.Build.Publishers;

[PublicAPI]
public class InvalidatingS3Publisher(
    IReadOnlyCollection<S3PublisherConfiguration> configurations,
    string invalidatedUrl )
    : S3Publisher( configurations )
{
    protected override bool Publish(
        BuildContext context,
        PublishSettings settings,
        (string Private, string Public) directories,
        BuildConfigurationInfo configuration,
        BuildInfo buildInfo,
        bool isPublic,
        ref bool hasTarget )
    {
        if ( !base.Publish( context, settings, directories, configuration, buildInfo, isPublic, ref hasTarget ) )
        {
            return false;
        }

        context.Console.WriteImportantMessage( $"{(settings.Dry ? "Dry run: " : "")}Invalidating {invalidatedUrl}" );

        if ( settings.Dry )
        {
            return true;
        }

        var url = Environment.ExpandEnvironmentVariables( invalidatedUrl );
        using var httpClient = new HttpClient();
        var invalidationResponse = httpClient.GetAsync( url ).GetAwaiter().GetResult();

        if ( invalidationResponse.StatusCode != System.Net.HttpStatusCode.OK )
        {
            context.Console.WriteError(
                $"Failed to invalidate {invalidatedUrl}: {invalidationResponse.StatusCode} {invalidationResponse.ReasonPhrase} / {invalidationResponse.Content.ReadAsString()}" );

            return false;
        }

        return true;
    }
}