// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools.Search.Crawlers;

public sealed class DocumentParserFactory
{
    private readonly Func<DocumentParser> _createParser;

    public DocumentParserFactory( Func<DocumentParser> createParser )
    {
        this._createParser = createParser;
    }

    public DocumentParser CreateParser() => this._createParser();
}