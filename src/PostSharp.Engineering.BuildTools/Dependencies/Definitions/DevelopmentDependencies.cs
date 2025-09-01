// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Dependencies.Model;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

[PublicAPI]
public static class DevelopmentDependencies
{
    public static ProductFamily Family { get; } = new( "Engineering", "2023.2" );

    public static DependencyDefinition PostSharpEngineering { get; } = new(
        Family,
        "PostSharp.Engineering",
        $"develop/{Family.Version}",
        $"release/{Family.Version}",
        new GitHubRepository( "PostSharp.Engineering", "postsharp" ),
        TeamCityHelper.CreateConfiguration(
            TeamCityHelper.GetProjectId( "PostSharp.Engineering", Family.Name ) ) )
    {
        GenerateSnapshotDependency = false,

        // Exclude PostSharp.Engineering from any recursive build since it necessarily fails with file locks.
        ExcludeFromRecursiveBuild = true,

        // Normally, dependencies to PostSharp.Engineering are 'feed' ones, i.e. the build configuration is irrelevant.
        // However, when the dependency source is overridden to a local repo copy, then we always want to run the debug build.
        DefaultConfigurationMapping = new ConfigurationSpecific<BuildConfiguration>(
            BuildConfiguration.Debug,
            BuildConfiguration.Debug,
            BuildConfiguration.Debug ),
        BuildOrder = int.MinValue
    };
}