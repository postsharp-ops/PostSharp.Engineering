// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;

namespace PostSharp.Engineering.BuildTools.Dependencies.Model;

public class CiProjectConfiguration
{
    public TeamCityProjectId ProjectId { get; }

    /// <summary>
    /// The project where the VCS root is stored.
    /// </summary>
    public string VcsRootProjectId { get; }

    public ConfigurationSpecific<string> BuildTypes { get; }

    public string? PullRequestStatusCheckBuildType { get; }

    public string? DeploymentBuildType { get; }

    public string? VersionBumpBuildType { get; }

    public string TokenEnvironmentVariableName { get; }

    public string BaseUrl { get; }

    public string UpstreamMergeBuildType => $"{this.ProjectId.Id}_UpstreamMerge";

    public CiProjectConfiguration(
        TeamCityProjectId projectId,
        ConfigurationSpecific<string> buildTypes,
        string? deploymentBuildType,
        string? versionBumpBuildType,
        string tokenEnvironmentVariableName,
        string baseUrl,
        bool pullRequestRequiresStatusCheck = true,
        string? pullRequestStatusCheckBuildType = null,
        string? vcsRootProjectId = null )
    {
        this.ProjectId = projectId;
        this.VcsRootProjectId = vcsRootProjectId ?? projectId.ParentId;
        this.BuildTypes = buildTypes;
        this.PullRequestStatusCheckBuildType = pullRequestRequiresStatusCheck ? pullRequestStatusCheckBuildType ?? $"{this.ProjectId}_DebugBuild" : null;
        this.DeploymentBuildType = deploymentBuildType;
        this.VersionBumpBuildType = versionBumpBuildType;
        this.TokenEnvironmentVariableName = tokenEnvironmentVariableName;
        this.BaseUrl = baseUrl;
    }
}