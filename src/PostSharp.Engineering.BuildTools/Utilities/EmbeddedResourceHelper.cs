// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Utilities;

internal static class EmbeddedResourceHelper
{
    public static void ExtractScript( BuildContext context, string fileName, string targetDirectory )
    {
        var product = context.Product;
        var replacements = new Dictionary<string, string>();
        replacements.Add( "<ENG_PATH>", product.EngineeringDirectory );
        replacements.Add( "<ENVIRONMENT_VARIABLES>", string.Join( ",", EnvironmentVariableNames.All.OrderBy( x => x ) ) );
        
        ExtractResource( context, fileName, targetDirectory, replacements );
    }

    public static void ExtractResource(
        BuildContext context,
        string fileName,
        string targetDirectory,
        IReadOnlyDictionary<string, string>? replacements = null )
    {
        var targetPath = Path.Combine( context.RepoDirectory, targetDirectory, fileName );

        using var resource = typeof(EmbeddedResourceHelper).Assembly.GetManifestResourceStream( $"PostSharp.Engineering.BuildTools.Resources.{fileName}" )
                             ?? throw new InvalidOperationException( $"Cannot find the resource {fileName}." );

        using var reader = new StreamReader( resource );
        var text = reader.ReadToEnd();

        if ( replacements != null )
        {
            foreach ( var replacement in replacements )
            {
                text = text.Replace( replacement.Key, replacement.Value, StringComparison.Ordinal );
            }
        }

        if ( !File.Exists( targetPath ) || File.ReadAllText( targetPath ) != text )
        {
            context.Console.WriteMessage( $"Writing '{targetPath}'." );

            File.WriteAllText( targetPath, text );
        }

        else

        {
            context.Console.WriteMessage( $"File '{targetPath}' is up to date." );
        }
    }
}