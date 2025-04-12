// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using HtmlAgilityPack;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Search.Crawlers;

public abstract class DocumentParser
{
    public abstract Task<IReadOnlyCollection<Snippet>> GetSnippetsFromDocument(
        HtmlDocument document,
        string source,
        string url,
        ImmutableArray<string> products );
}