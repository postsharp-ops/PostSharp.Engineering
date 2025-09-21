// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Dependencies.Model;

namespace PostSharp.Engineering.BuildTools.Dependencies
{
    /// <summary>
    /// Fetches the artifacts of the build dependencies.
    /// </summary>
    internal abstract class BaseFetchDependencyCommand : BaseCommand<FetchDependenciesCommandSettings>
    {
        protected abstract bool Update { get; }

        protected override bool ExecuteCore( BuildContext context, FetchDependenciesCommandSettings settings )
        {
            context.Console.WriteHeading( "Fetching build artifacts" );

            if ( !settings.TryGetBuildConfiguration( context, out var configuration ) )
            {
                return false;
            }

            if ( !DependenciesConfigurationFile.TryLoad( context, settings, configuration, out var dependenciesOverrideFile ) )
            {
                return false;
            }

            if ( !DependenciesHelper.UpdateOrFetchDependencies( context, configuration, dependenciesOverrideFile, this.Update ) )
            {
                return false;
            }

            if ( !dependenciesOverrideFile.TrySave( context, settings ) )
            {
                return false;
            }

            return true;
        }
    }
}