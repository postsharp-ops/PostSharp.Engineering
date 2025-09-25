// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Publishing;
using PostSharp.Engineering.BuildTools.Build.Swapping;
using PostSharp.Engineering.BuildTools.Build.Triggers;
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
public record BuildConfigurationInfo(

    // ReSharper disable once InconsistentNaming
    bool RequiresSigning = false,
    IBuildTrigger[]? BuildTriggers = null,

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
    TimeSpan? SwapTimeout = null );