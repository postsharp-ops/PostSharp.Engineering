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
        DependenciesOverrideFile dependenciesOverrideFile,
        DependencyDefinition dependencyDefinition,
        ResetDependenciesCommandSettings settings,
        DependenciesOverrideFile defaultDependenciesOverrideFile )
    {
        if ( defaultDependenciesOverrideFile.Dependencies.TryGetValue( dependencyDefinition.Name, out var defaultSource ) )
        {
            dependenciesOverrideFile.Dependencies[dependencyDefinition.Name] = defaultSource;
        }
        else
        {
            dependenciesOverrideFile.Dependencies.Remove( dependencyDefinition.Name );
        }

        return true;
    }
}