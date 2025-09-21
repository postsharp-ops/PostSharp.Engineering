// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.Build.Files;

internal class BumpInfoFile
{
    public Dictionary<string, Version> Dependencies { get; init; } = new();

    public BumpInfoFile( Dictionary<string, Version> dependencies ) { this.Dependencies = dependencies; }

    public BumpInfoFile() { }

    public static BumpInfoFile? Parse( string text )
    {
        try
        {
            return JsonConvert.DeserializeObject<BumpInfoFile>( text );
        }
        catch
        {
            return null;
        }
    }

    public override string ToString() => JsonConvert.SerializeObject( this );
}