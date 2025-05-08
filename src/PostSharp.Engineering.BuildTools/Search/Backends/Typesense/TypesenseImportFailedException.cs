// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Typesense;

namespace PostSharp.Engineering.BuildTools.Search.Backends.Typesense;

public class TypesenseImportFailedException : Exception
{
    private readonly Dictionary<int, ImportResponse> _data;

    public TypesenseImportFailedException( IReadOnlyCollection<ImportResponse> failedResponses ) :
        base( $"Import failed: {string.Join( "", failedResponses.Select( x => $"\n - {x.Error}" ) )}" )
    {
        var i = 0;
        this._data = failedResponses.ToDictionary( _ => i++, r => r );
    }

    public override IDictionary Data => this._data;

    public override string ToString()
        => string.Join(
            $"{Environment.NewLine}{Environment.NewLine}",
            Enumerable.Empty<string>()
                .Append( base.ToString() )
                .Concat( this._data.Values.Select( r => $"{r.Error}{Environment.NewLine}{r.Document}" ) ) );
}