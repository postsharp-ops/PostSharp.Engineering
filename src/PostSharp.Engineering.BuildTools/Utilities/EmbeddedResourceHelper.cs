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

        // Combine standard environment variables with product-specific additional ones
        var allEnvironmentVariables = EnvironmentVariableNames.All
            .Concat( product.AdditionalDockerEnvironmentVariables )
            .OrderBy( x => x );

        replacements.Add( "<ENVIRONMENT_VARIABLES>", string.Join( ",", allEnvironmentVariables ) );
        replacements.Add( "<PRODUCT_NAME>", product.ProductNameWithoutDot );

        // Image-name prefix for chained Dockerfiles: stems are "{prefix}-{layer}". Must match
        // ContainerRequirements.GetImagePrefix / the default DockerSpec.ImageName.
        replacements.Add( "<DOCKER_IMAGE_PREFIX>", $"{product.ProductNameWithoutDot}-{product.ProductFamily.Version}".ToLowerInvariant() );

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

        TextFileHelper.WriteIfDifferent( targetPath, text, context );
    }
}