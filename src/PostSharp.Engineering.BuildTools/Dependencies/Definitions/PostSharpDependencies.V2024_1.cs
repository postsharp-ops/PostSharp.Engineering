// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

public static partial class PostSharpDependencies
{
    // ReSharper disable once InconsistentNaming

    [Obsolete("PostSharp 2024.1 is no longer maintained or built.")]
    [PublicAPI]
    public static class V2024_1
    {
        private class PostSharpDependencyDefinition : DependencyDefinition
        {
            private static readonly TeamCityProjectId _teamCityProjectId = new(
                $"PostSharpGitHub_PostSharp{Family.VersionWithoutDots}",
                "PostSharpGitHub" );

            private static readonly string _distributionBuildId = $"{_teamCityProjectId}_BuildDistribution";

            public PostSharpDependencyDefinition()
                : base(
                    Family,
                    "PostSharpPackage",
                    $"refs/heads/release/{Family.Version}",
                    null,
                    new AzureDevOpsRepository( _projectName, _projectName ),
                    new CiProjectConfiguration(
                        _teamCityProjectId,
                        new ConfigurationSpecific<string>( "not-used", _distributionBuildId, "not-used" ),
                        null,
                        null,
                        TeamCityHelper.TeamCityCloudTokenEnvironmentVariableName,
                        TeamCityHelper.TeamCityCloudUrl ),
                    false )
            {
                this.EngineeringDirectory = @"PrivateBuild\Distribution\eng";
            }
        }

        public static ProductFamily Family { get; } = new( _projectName, "2024.1", DevelopmentDependencies.Family );

        public static DependencyDefinition PostSharp { get; } = new PostSharpDependencyDefinition();
    }
}