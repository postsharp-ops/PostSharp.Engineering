// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Build.Files.NuGet;

/// <summary>
/// Writes <c>nuget.config</c>.
/// </summary>
internal static class NuGetConfigFile
{
    internal static bool TryWrite( BuildContext context, DependenciesConfigurationFile dependenciesConfigurationFile, BuildConfiguration configuration )
    {
        var product = context.Product;

        if ( !product.GenerateNuGetConfig )
        {
            return true;
        }

        // Fetch to resolve the VersionFile properties.
        if ( !dependenciesConfigurationFile.Fetch( context ) )
        {
            return false;
        }

        // Generate standard nuget.config
        var standardGenerator = new StandardNuGetConfigGenerator();

        if ( !standardGenerator.TryGenerate( context, dependenciesConfigurationFile, configuration ) )
        {
            return false;
        }

        // Generate WSL version if AddWslSupport is enabled
        if ( product.AddWslSupport )
        {
            var wslGenerator = new WslNuGetConfigGenerator();

            if ( !wslGenerator.TryGenerate( context, dependenciesConfigurationFile, configuration ) )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Generates nuget.restored.config for TeamCity artifact restoration scenario.
    /// This file uses relative paths and assumes dependencies are in dependencies/&lt;name&gt;/ directory.
    /// </summary>
    internal static bool TryWriteRestoredArtifacts(
        BuildContext context,
        DependenciesConfigurationFile dependenciesConfigurationFile,
        BuildConfiguration configuration )
    {
        var generator = new RestoredArtifactsNuGetConfigGenerator();

        return generator.TryGenerate( context, dependenciesConfigurationFile, configuration );
    }
}