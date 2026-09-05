// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Arguments;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Triggers;
using PostSharp.Engineering.BuildTools.Docker;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

[PublicAPI]
public abstract class AdditionalCiBuildConfiguration
{
    public string Name { get; }

    public string Id { get; }

    public string? Branch { get; init; }

    public SourceDependenciesRequirements SourceDependenciesRequirements { get; init; }

    /// <summary>
    /// Gets or sets the build configuration on which the current <see cref="AdditionalCiBuildConfiguration"/> depends.
    /// </summary>
    public BuildConfiguration? BuildSnapshotDependency { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the snapshot dependency should accept the last successful build
    /// regardless of the current source snapshot. When <c>true</c>, TeamCity will reuse the last successful build
    /// of the dependency instead of requiring a build from the exact same source revision.
    /// </summary>
    public bool ReuseLastSuccessfulBuild { get; init; }

    public bool OnlyCheckoutEngineering { get; init; }

    protected AdditionalCiBuildConfiguration( string id, string name )
    {
        this.Id = id;
        this.Name = name;
    }

    internal abstract TeamCityBuildConfiguration TeamCityBuildConfiguration(
        ProductProperties productProperties,
        IReadOnlyDictionary<BuildConfiguration, TeamCityBuildConfiguration> teamCityBuildBuildConfigurations );

    public BuildAgentRequirements? BuildAgentRequirements { get; init; }

    public BuildConfigurationParameter[]? Parameters { get; init; }

    /// <summary>
    /// Gets the wall-clock limit of the build in minutes, or <c>null</c> for no limit. Without one a build that
    /// hangs holds its agent indefinitely; with one that is too short a legitimately long build is killed.
    /// </summary>
    public int? TimeoutInMinutes { get; init; }

    /// <summary>
    /// Gets the memory limit of the container in gigabytes, or <c>null</c> for the default. Test cells differ:
    /// the time-sensitive ones are given less so that several fit on one agent, while the build-heavy ones need
    /// more, because a container that runs out of memory does not merely slow down -- it mis-sizes the build
    /// parallelism and then fails with an out-of-memory error from an unrelated process.
    /// </summary>
    public int? ContainerMemoryInGigabytes { get; init; }

    /// <summary>
    /// Gets the artifact rule by which this configuration consumes the artifacts of its
    /// <see cref="BuildSnapshotDependency"/>, or <c>null</c> to take the whole private artifact directory as it
    /// is. A distribution test sets it, because what it needs is the content of the shipped archive rather than
    /// the archive itself -- for example <c>PostSharp*.7z!**/* =&gt;</c>, where the <c>!</c> tells TeamCity to
    /// extract rather than copy.
    /// </summary>
    public string? DependencyArtifactRules { get; init; }

    /// <summary>
    /// Gets a value indicating whether TeamCity empties the destination of each artifact rule before downloading.
    /// The default is <c>true</c>. Set it to <c>false</c> where a rule unpacks an archive into the checkout root,
    /// because the clean would then delete the sources; the build fails afterwards on a missing file, with
    /// nothing to say that the checkout was emptied.
    /// </summary>
    public bool CleanDependencyDestination { get; init; } = true;

    /// <summary>
    /// Gets the display name of the sub-project this configuration belongs to, or <c>null</c> to sit at the root
    /// of the generated project. Configurations that share a folder are grouped into one TeamCity sub-project, so
    /// that a product with dozens of test cells does not present them as one flat list.
    /// </summary>
    public string? ProjectFolder { get; init; }

    /// <summary>
    /// Gets the identifier of another <see cref="AdditionalCiBuildConfiguration"/> whose artifacts this one
    /// consumes, instead of one of the product build configurations named by <see cref="BuildSnapshotDependency"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A product whose pipeline has intermediate stages needs this. PostSharp builds artifacts, then assembles a
    /// distribution from them, then signs it; the first two are far cheaper than the third, so a test that only
    /// needs the artifacts must be able to depend on the stage that produced them rather than on the signed build
    /// at the end of the chain. Without it every cell would wait for the most expensive stage, and a failure could
    /// only be iterated on by re-running the whole pipeline.
    /// </para>
    /// <para>
    /// This and <see cref="BuildSnapshotDependency"/> answer different questions and can be set together: this
    /// one names the configuration to wait for, while <see cref="BuildSnapshotDependency"/> selects whose
    /// artifact layout is read. Where only this one is set, the layout of the public configuration is assumed,
    /// which is right for a product that produces nothing else.
    /// </para>
    /// </remarks>
    public string? BuildSnapshotDependencyId { get; init; }

    /// <summary>
    /// Gets the triggers that start this build configuration, or <c>null</c> for one that is only ever started by
    /// hand. A <see cref="NightlyBuildTrigger"/> here is what turns an additional configuration from a button into
    /// a scheduled job.
    /// </summary>
    /// <remarks>
    /// Worth setting <see cref="ReuseLastSuccessfulBuild"/> alongside a schedule. A nightly build fires against
    /// whatever the branch holds at the time, and without it the snapshot dependency demands a build of that exact
    /// revision, so a quiet repository queues a fresh build of the dependency every night for nothing.
    /// </remarks>
    public IBuildTrigger[]? BuildTriggers { get; init; }

    public string? Dockerfile { get; init; }

    /// <summary>
    /// Gets the GitHub App connection and parameter that replace the ones inherited from the repository, or <c>null</c>
    /// to use the repository's own. A build configuration issues a single build-scoped token, so setting this
    /// substitutes the identity of the token rather than adding a second one.
    /// </summary>
    public GitHubAppTokenOverride? GitHubAppToken { get; init; }

    /// <summary>
    /// Gets the TeamCity artifact rules for this configuration, or <c>null</c> to publish nothing. One rule per
    /// entry, such as <c>+:artifacts/preflight.log</c>.
    /// </summary>
    /// <remarks>
    /// An additional configuration publishes nothing by default, which is right for one whose whole result is its
    /// exit code and wrong for one that writes a file somebody will want afterwards. The case that prompted this is
    /// a nightly job whose script transcribes itself to <c>artifacts/preflight.log</c> for the agent that runs next
    /// in the same container: the file dies with the container, so when a run reported that transcript as empty
    /// while the build log showed the opposite, there was no way to tell which of them was right.
    /// </remarks>
    public string[]? ArtifactRules { get; init; }
}