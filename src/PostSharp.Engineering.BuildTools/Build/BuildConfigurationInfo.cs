// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Publishing;
using PostSharp.Engineering.BuildTools.Build.Swapping;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Triggers;
using System;

namespace PostSharp.Engineering.BuildTools.Build;

/// <summary>
/// Characteristics of a <see cref="BuildConfiguration"/>.
/// </summary>
/// <param name="RequiresSigning">Determines whether artifacts in this build configuration must be signed.</param>
/// <param name="BuildTriggers">List of build triggers to be configured in the build server.</param>
/// <param name="PublicPublishers">List of publishers of public artefacts.</param>
/// <param name="PrivatePublishers">List of publishers of private artefacts.</param>
/// <param name="Swappers">List of swappers, i.e. logic that swaps a staging environment into a production environment.</param>
/// <param name="TeamCityBuildName">Name of the TeamCity configuration implementing the <b>Build</b> action.</param>
/// <param name="TeamCityDeploymentName">Name of the TeamCity configuration implementing the <b>Deploy</b> action.</param>
/// <param name="TeamCitySwapName">Name of the TeamCity configuration implementing the <b>Swap</b> action.</param>
/// <param name="VersionKind">
/// The versioning scheme of a build of this configuration on the build server, or <c>null</c> for the default:
/// <see cref="Model.VersionKind.Public"/> for <see cref="BuildConfiguration.Public"/> and <see cref="Model.VersionKind.Numbered"/>
/// for the others. A product whose tested build and signed build must carry the same version, so that the two can be
/// compared, gives <see cref="Model.VersionKind.Public"/> to the configuration of its tested build. A local build of a
/// configuration other than <see cref="BuildConfiguration.Public"/> always has a local version, so that it never puts a
/// package of a public version in the package cache.
/// </param>
/// <param name="SupportsBuild">
/// Determines whether <c>Build.ps1 build</c>, <c>test</c> and <c>prepare</c> accept this configuration. A product sets it
/// to <c>false</c> for a configuration that its build configuration on the build server (<see cref="CustomBuildConfiguration"/>)
/// produces otherwise, for example a public build that signs the packages of another build instead of building, so that
/// nobody builds the configuration locally and gets something other than what ships. <c>sign</c> and the publishing
/// commands still accept it.
/// </param>
/// <param name="DeploymentDependencies">
/// The build configurations of the same product that the deployments of this configuration wait for, besides its build,
/// or <c>null</c> for none. A product whose shipped build is tested by a test matrix of its own names the quality gate of
/// that matrix here, so that nothing is deployed before its tests pass. A dependency without <see cref="SnapshotDependency.ArtifactRules"/>
/// downloads nothing, which is what an ordering dependency on a quality gate needs; the deployment takes its artifacts from
/// the build alone. The standalone deployment (<see cref="ExportsToTeamCityDeployWithoutDependencies"/>) ignores them.
/// </param>
/// <param name="CustomBuildConfiguration">
/// The build configuration that replaces the standard <b>Build</b> action, or <c>null</c> for the standard one. A
/// product whose public build consumes what an earlier build configuration of the same product produced -- PostSharp
/// signs a distribution that an earlier configuration assembled and tested -- cannot be described by the standard
/// build step, which builds from source and depends on nothing.
/// </param>
/// <remarks>
/// The replacement states its own build steps, parameters, timeout, agent requirements and Docker specification.
/// What identifies the build configuration to the rest of the product is not its to choose and is overridden: the
/// object name, from which TeamCity derives the build type identifier that other products depend on; the display
/// name; the default branch; the published artifact rules, which the deployment reads by name; the dependencies on
/// other products; the source dependencies; and the commit status publisher. Extra artifact rules go in
/// <see cref="AdditionalArtifactRules"/> and triggers in <see cref="BuildTriggers"/>, both of which the generator
/// applies to the replacement as it does to the standard configuration.
/// </remarks>
public record BuildConfigurationInfo(
    bool RequiresSigning = false,
    IBuildTrigger[]? BuildTriggers = null,
    Pattern? PublicArtifacts = null,
    Pattern? PrivateArtifacts = null,

    // Publishers for public artifacts.
    Publisher[]? PublicPublishers = null,

    // Publishers for private artifacts.
    Publisher[]? PrivatePublishers = null,
    Swapper[]? Swappers = null,
    string? TeamCityBuildName = null,
    string? TeamCityDeploymentName = null,
    string? TeamCitySwapName = null,
    bool SwapAfterPublishing = false,
    string[]? AdditionalArtifactRules = null,
    bool ExportsToTeamCityBuild = true,
    bool ExportsToTeamCityDeploy = true,
    bool ExportsToTeamCityDeployWithoutDependencies = false,
    bool RequiresUpstreamCheck = false,
    TimeSpan? BuildTimeout = null,
    TimeSpan? DeploymentTimeout = null,
    TimeSpan? SwapTimeout = null,
    AdditionalCiBuildConfiguration? CustomBuildConfiguration = null,
    VersionKind? VersionKind = null,
    bool SupportsBuild = true,
    SnapshotDependency[]? DeploymentDependencies = null );