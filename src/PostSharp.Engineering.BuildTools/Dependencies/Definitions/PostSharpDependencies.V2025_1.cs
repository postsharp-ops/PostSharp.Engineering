// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Model;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

public static partial class PostSharpDependencies
{
    // ReSharper disable once InconsistentNaming

    [PublicAPI]
    public static class V2025_1_GitHub
    {
        private class PostSharpDependencyDefinition : DependencyDefinition
        {
            private static readonly TeamCityProjectId _teamCityProjectId = new(
                $"{_projectName}_{_projectName}{Family.VersionWithoutDots}",
                _projectName );

            private static readonly string _distributionBuildId = $"{_teamCityProjectId}_BuildDistribution";

            public PostSharpDependencyDefinition()
                : base(
                    Family,
                    "PostSharpPackage",
                    $"release/{Family.Version}",
                    null,
                    new GitHubRepository( _projectName, _projectName ),
                    new CiProjectConfiguration(
                        _teamCityProjectId,
                        new ConfigurationSpecific<string>( "not-used", _distributionBuildId, "not-used" ),
                        null,
                        null,
                        TeamCityHelper.TeamCityCloudTokenEnvironmentVariableName,
                        TeamCityHelper.TeamCityCloudUrl ),
                    false )
            {
                this.EngineeringDirectory = @"Build\Distribution\eng";
            }
        }

        public static ProductFamily Family { get; } = new( _projectName, "2025.1", DevelopmentDependencies.Family );

        public static DependencyDefinition PostSharp { get; } = new PostSharpDependencyDefinition();
    }
}