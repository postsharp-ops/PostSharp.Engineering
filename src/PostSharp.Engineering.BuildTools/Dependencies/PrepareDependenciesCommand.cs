using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Dependencies.Model;

namespace PostSharp.Engineering.BuildTools.Dependencies;

/// <summary>
/// Generates the dependency files with the current settings.
/// </summary>
[UsedImplicitly]
internal class PrepareDependenciesCommand : ConfigureDependenciesCommand<ConfigureDependenciesCommandSettings>
{
    protected override bool ConfigureDependency(
        BuildContext context,
        DependenciesConfigurationFile dependenciesConfigurationFile,
        DependencyDefinition dependencyDefinition,
        ConfigureDependenciesCommandSettings settings,
        DependenciesConfigurationFile defaultDependenciesConfigurationFile )
        => true;
}