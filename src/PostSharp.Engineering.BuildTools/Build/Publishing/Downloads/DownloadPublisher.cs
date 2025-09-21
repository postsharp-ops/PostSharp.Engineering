// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.Build.Publishing.Downloads;

[PublicAPI]
public class DownloadPublisher : InvalidatingS3Publisher
{
    public DownloadPublisher( IReadOnlyCollection<S3PublisherConfiguration> configurations ) : base(
        configurations,
        "https://www.postsharp.net/download/Refresh.ashx?p=%DOWNLOADS_API_KEY%" ) { }
}