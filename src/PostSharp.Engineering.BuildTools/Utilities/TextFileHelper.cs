// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System;
using System.IO;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Utilities;

internal static class TextFileHelper
{
    public static bool WriteIfDifferent( string path, XDocument content, BuildContext context, bool dry = false )
        => WriteIfDifferent( path, content.ToNiceString(), context, dry );

    public static bool WriteIfDifferent( string path, string content, BuildContext context, bool dry = false )
    {
        // Compare line-ending-insensitive: on Windows with autocrlf, the on-disk file can have CRLF while
        // generated content has LF, making the strings differ even though git considers them identical.
        // Without this, we report "changed" but the subsequent `git add`/`git commit` becomes a no-op.
        static string NormalizeLineEndings( string s ) => s.Replace( "\r\n", "\n", StringComparison.Ordinal );

        if ( File.Exists( path ) && NormalizeLineEndings( content ) == NormalizeLineEndings( File.ReadAllText( path ) ) )
        {
            context.Console.WriteMessage( $"The file '{path}' is up to date." );

            return false;
        }
        else
        {
            if ( dry )
            {
                context.Console.WriteMessage( $"Dry run: would write '{path}'." );
            }
            else
            {
                context.Console.WriteMessage( $"Writing '{path}'." );
                Directory.CreateDirectory( Path.GetDirectoryName( path )! );
                File.WriteAllText( path, content );
            }

            return true;
        }
    }
}