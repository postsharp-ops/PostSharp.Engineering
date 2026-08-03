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

    /// <summary>
    /// Gets the identifier of the VCS root, or <c>null</c> when it follows the default convention and is therefore
    /// derived from <see cref="VcsRootProjectId"/> and the repository name by <see cref="Tools.TeamCity.TeamCityHelper.GetVcsId(DependencyDefinition)"/>.
    /// This is set only for products whose VCS root does not follow that convention, typically because their family
    /// has no per-product project level.
    /// </summary>
    public string? VcsRootId { get; }

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
        string? vcsRootProjectId = null,
        string? vcsRootId = null )
    {
        this.ProjectId = projectId;
        this.VcsRootProjectId = vcsRootProjectId ?? projectId.ParentId;
        this.VcsRootId = vcsRootId;
        this.BuildTypes = buildTypes;
        this.PullRequestStatusCheckBuildType = pullRequestRequiresStatusCheck ? pullRequestStatusCheckBuildType ?? $"{this.ProjectId}_DebugBuild" : null;
        this.DeploymentBuildType = deploymentBuildType;
        this.VersionBumpBuildType = versionBumpBuildType;
        this.TokenEnvironmentVariableName = tokenEnvironmentVariableName;
        this.BaseUrl = baseUrl;
    }
}