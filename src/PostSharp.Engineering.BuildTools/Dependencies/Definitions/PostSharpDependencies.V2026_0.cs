// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

public static partial class PostSharpDependencies
{
    // ReSharper disable once InconsistentNaming

    [PublicAPI]
    public static class V2026_0
    {
        private class PostSharpDependencyDefinition : DependencyDefinition
        {
            private static readonly TeamCityProjectId _teamCityProjectId = new(
                $"PostSharpGitHub_{_projectName}{Family.VersionWithoutDots}",
                "PostSharpGitHub" );

            private static readonly string _distributionBuildId = $"{_teamCityProjectId}_BuildDistribution";

            public PostSharpDependencyDefinition()
                : base(
                    Family,
                    "PostSharpPackage",
                    $"refs/heads/release/{Family.Version}",
                    null,
                    new GitHubRepository( _projectName, _projectName ),
                    new CiProjectConfiguration(
                        _teamCityProjectId,
                        new ConfigurationSpecific<string>( "not-used", _distributionBuildId, "not-used" ),
                        null,
                        null,
                        EnvironmentVariableNames.TeamCityToken,
                        TeamCityHelper.TeamCityCloudUrl ),
                    false )
            {
                this.EngineeringDirectory = @"Build\Distribution\eng";
                this.PackagePatterns = ["PostSharp", "PostSharp.Redist", "PostSharp.Compiler.*", "PostSharp.Patterns.*", "PostSharp.Settings.*"];
            }
        }

        public static ProductFamily Family { get; } = new( _projectName, "2026.0", DevelopmentDependencies.Family );

        public static DependencyDefinition PostSharp { get; } = new PostSharpDependencyDefinition();
    }
}