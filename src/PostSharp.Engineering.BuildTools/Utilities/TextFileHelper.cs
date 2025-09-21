// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Utilities;

internal static class TextFileHelper
{
    public static void WriteIfDifferent( string path, string content, BuildContext context )
    {
        if ( File.Exists( path ) && content == File.ReadAllText( path ) )
        {
            context.Console.WriteMessage( $"The file '{path}' is up to date." );
        }
        else
        {
            context.Console.WriteMessage( $"Writing '{path}'." );
            Directory.CreateDirectory( Path.GetDirectoryName( path )! );
            File.WriteAllText( path, content );
        }
    }
}