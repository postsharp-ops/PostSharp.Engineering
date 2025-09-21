// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Dependencies.Model;

namespace PostSharp.Engineering.BuildTools.Dependencies;

/// <summary>
/// Removes the configuration of a dependency from the version file.
/// </summary>
[UsedImplicitly]
internal class ResetDependenciesCommand : ConfigureDependenciesCommand<ResetDependenciesCommandSettings>
{
    protected override bool ConfigureDependency(
        BuildContext context,
        DependenciesConfigurationFile dependenciesConfigurationFile,
        DependencyDefinition dependencyDefinition,
        ResetDependenciesCommandSettings settings,
        DependenciesConfigurationFile defaultDependenciesConfigurationFile )
    {
        if ( defaultDependenciesConfigurationFile.Dependencies.TryGetValue( dependencyDefinition.Name, out var defaultSource ) )
        {
            dependenciesConfigurationFile.Dependencies[dependencyDefinition.Name] = defaultSource;
        }
        else
        {
            dependenciesConfigurationFile.Dependencies.Remove( dependencyDefinition.Name );
        }

        return true;
    }
}