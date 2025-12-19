// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Publishing;

[PublicAPI]
public class DocumentationPublisher : S3Publisher
{
    private readonly string _documentationUrl;

    public DocumentationPublisher( IReadOnlyCollection<S3PublisherConfiguration> configurations, string documentationUrl )
        : base( configurations )
    {
        this._documentationUrl = documentationUrl;
    }

    private string InvalidateUrl => $"{this._documentationUrl}_api/invalidate?%{EnvironmentVariableNames.DocInvalidationKey}%";

    public override void AddDependencies( List<Publisher> publishers, int currentIndex )
    {
        if ( !publishers.Skip( currentIndex )
                .Any( p => p is HttpGetPublisher invalidator && invalidator.Url.StartsWith( this._documentationUrl, StringComparison.Ordinal ) ) )
        {
            publishers.Add( new HttpGetPublisher( this.InvalidateUrl ) );
        }
    }
}