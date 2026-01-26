// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Files.NuGet;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Dependencies;

/// <summary>
/// Base class for <see cref="SetDependenciesCommand"/> and <see cref="ResetDependenciesCommand"/>.
/// </summary>
/// <typeparam name="T"></typeparam>
internal abstract class ConfigureDependenciesCommand<T> : BaseCommand<T>
    where T : ConfigureDependenciesCommandSettings
{
    protected override bool ExecuteCore( BuildContext context, T settings )
    {
        var console = context.Console;
        var product = context.Product;

        // Validates the command line options.

        console.WriteHeading( "Setting the local dependencies" );

        if ( product.ParametrizedDependencies is not { Length: > 0 } )
        {
            console.WriteError( "This product has no dependency." );

            return false;
        }

        if ( settings.GetDependencies().Length == 0 && !settings.GetAllFlag() )
        {
            console.WriteError( "No dependency was specified. Specify a dependency or use --all." );

            return false;
        }

        if ( !settings.TryGetBuildConfiguration( context, out var configuration ) )
        {
            return false;
        }

        // Loads the default dependencies.
        if ( !DependenciesConfigurationFile.TryLoadDefaultsOnly( context, settings, configuration, out var defaultDependenciesOverrideFile ) )
        {
            return false;
        }

        // Loads the current version file.
        if ( !DependenciesConfigurationFile.TryLoad( context, settings, configuration, out var dependenciesOverrideFile ) )
        {
            return false;
        }

        // Iterate all matching dependencies.
        var dependencies = settings.GetAllFlag() ? product.ParametrizedDependencies.Select( x => x.Name ) : settings.GetDependencies();

        foreach ( var dependencyName in dependencies )
        {
            ParametrizedDependency? dependency;

            if ( int.TryParse( dependencyName, out var index ) )
            {
                // The dependency was given by position.

                if ( index < 1 || index > product.ParametrizedDependencies.Length )
                {
                    console.WriteError( $"'{index}' is not a valid dependency index. Use the 'dependencies list' command." );

                    return false;
                }

                dependency = product.ParametrizedDependencies[index - 1];
            }
            else
            {
                // The dependency was given by name.

                if ( !product.TryGetDependency( dependencyName, out dependency ) )
                {
                    console.WriteError( $"'{dependencyName}' is not a valid dependency name for this product. Use the 'dependencies list' command." );

                    return false;
                }
            }

            // Executes the logic itself.
            if ( !this.ConfigureDependency( context, dependenciesOverrideFile, dependency, settings, defaultDependenciesOverrideFile ) )
            {
                return false;
            }
        }

        // Remove transitive dependencies.
        foreach ( var transitiveDependency in dependenciesOverrideFile.Dependencies.Keys
                     .Where( dependency => !defaultDependenciesOverrideFile.Dependencies.ContainsKey( dependency ) )
                     .ToList() )
        {
            console.WriteMessage( $"Resetting transitive dependency '{transitiveDependency}'." );
            dependenciesOverrideFile.Dependencies.Remove( transitiveDependency );
        }

        // Updating dependencies.
        console.WriteImportantMessage( "Updating dependencies" );

        if ( !DependenciesHelper.UpdateOrFetchDependencies( context, configuration, dependenciesOverrideFile, true ) )
        {
            return false;
        }

        // Writing the version file.
        if ( !dependenciesOverrideFile.TryWrite( context ) )
        {
            return false;
        }

        // Writing the configurations neutral file.
        ConfigurationNeutralVersionFile.Write( context, settings, configuration );

        // Generate nuget.config.
        if ( !NuGetConfigFile.TryWrite( context, dependenciesOverrideFile, configuration ) ||
             !GlobalJsonFile.TryWrite( context ) )
        {
            return false;
        }

        console.WriteLine();

        dependenciesOverrideFile.Print( context );

        console.WriteSuccess( "Setting dependencies was successful." );

        return true;
    }

    protected abstract bool ConfigureDependency(
        BuildContext context,
        DependenciesConfigurationFile dependenciesConfigurationFile,
        DependencyDefinition dependencyDefinition,
        T settings,
        DependenciesConfigurationFile defaultDependenciesConfigurationFile );
}