// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Publishing.Downloads;

[PublicAPI]
public class DownloadPublisher : S3Publisher
{
    public DownloadPublisher( IReadOnlyCollection<S3PublisherConfiguration> configurations ) : base( configurations ) { }

    private static string InvalidationUrl => $"https://www.postsharp.net/download/Refresh.ashx?p=%{EnvironmentVariableNames.DownloadsInvalidationKey}%";

    public override void AddDependencies( List<Publisher> publishers, int currentIndex )
    {
        var invalidationUrl = InvalidationUrl;

        if ( !publishers.Skip( currentIndex )
                .Any( p => p is HttpGetPublisher invalidator && invalidator.Url.Equals( invalidationUrl, StringComparison.Ordinal ) ) )
        {
            publishers.Add( new HttpGetPublisher( invalidationUrl ) );
        }
    }
}