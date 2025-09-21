// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.Build.Publishing;

[PublicAPI]
public class DocumentationPublisher : InvalidatingS3Publisher
{
    public DocumentationPublisher( IReadOnlyCollection<S3PublisherConfiguration> configurations, string documentationUrl )
        : base( configurations, $"{documentationUrl}_api/invalidate?%DOC_API_KEY%" ) { }
}