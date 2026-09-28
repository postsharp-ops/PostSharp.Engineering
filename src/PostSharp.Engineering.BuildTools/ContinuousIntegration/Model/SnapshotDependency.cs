// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

/// <summary>
/// A dependency of one build configuration of a product on another build configuration of the <i>same</i> product:
/// it waits for that configuration and downloads its artifacts.
/// </summary>
/// <remarks>
/// <para>
/// A product whose pipeline has intermediate build configurations needs this in both directions. PostSharp builds
/// artifacts, then assembles a distribution from them, then signs it: a test cell must be able to depend on the
/// cheap configuration that produced what it consumes rather than on the expensive one at the end of the chain, and
/// the signed build must be able to depend on the distribution it signs. Both are the same relation, so both are
/// this type. An <see cref="AdditionalCiBuildConfiguration"/> declares them through
/// <see cref="AdditionalCiBuildConfiguration.SnapshotDependencies"/>.
/// </para>
/// <para>
/// This is distinct from a dependency on another product, which is declared on the dependency definition and
/// addressed by absolute TeamCity identifier. The target here lives in the same generated project, so it is
/// addressed by the object name of its build type.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record SnapshotDependency
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotDependency"/> class targeting the
    /// <see cref="AdditionalCiBuildConfiguration"/> whose <see cref="AdditionalCiBuildConfiguration.Id"/> is
    /// <paramref name="configurationId"/>.
    /// </summary>
    public SnapshotDependency( string configurationId )
    {
        this.ConfigurationId = configurationId ?? throw new ArgumentNullException( nameof(configurationId) );
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotDependency"/> class targeting one of the product's own
    /// build configurations. That configuration must have <see cref="BuildConfigurationInfo.ExportsToTeamCityBuild"/>
    /// set, because a configuration that is not exported has no build type to depend on.
    /// </summary>
    public SnapshotDependency( BuildConfiguration configuration )
    {
        this.Configuration = configuration;
    }

    /// <summary>
    /// Gets the <see cref="AdditionalCiBuildConfiguration.Id"/> of the target, or <c>null</c> when the target is one
    /// of the product's own build configurations, named by <see cref="Configuration"/>.
    /// </summary>
    public string? ConfigurationId { get; }

    /// <summary>
    /// Gets the product build configuration that is the target, or <c>null</c> when the target is an additional
    /// configuration named by <see cref="ConfigurationId"/>.
    /// </summary>
    public BuildConfiguration? Configuration { get; }

    /// <summary>
    /// Gets the TeamCity artifact rules by which the dependent configuration consumes the artifacts of this target,
    /// one rule per entry, or <c>null</c> to take the whole private artifact directory as it is. An empty array
    /// downloads nothing, which makes this an ordering dependency only.
    /// </summary>
    /// <remarks>
    /// A distribution test sets it, because what it needs is the content of the shipped archive rather than the
    /// archive itself, for example <c>PostSharp-*.7z!**=&gt;</c>, where the <c>!</c> tells TeamCity to extract rather
    /// than copy. The entries are joined by the generator; do not embed a separator yourself, and do not write a line
    /// break in a rule, because the rules are emitted into a single-line string.
    /// </remarks>
    public string[]? ArtifactRules { get; init; }

    /// <summary>
    /// Gets a value indicating whether TeamCity empties the destination of each artifact rule before downloading.
    /// The default is <c>true</c>. Set it to <c>false</c> where a rule unpacks an archive into the checkout root,
    /// because the clean would then delete the sources; the build fails afterwards on a missing file, with nothing
    /// to say that the checkout was emptied.
    /// </summary>
    /// <remarks>
    /// Per target rather than per configuration, because TeamCity applies it per artifact dependency and one
    /// configuration can legitimately want it both ways, unpacking one target into the checkout root while taking
    /// another into a directory of its own.
    /// </remarks>
    public bool CleanDestination { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the dependency accepts the last successful build of the target regardless of
    /// the current source revision, or <c>null</c> to inherit
    /// <see cref="AdditionalCiBuildConfiguration.ReuseLastSuccessfulBuild"/>.
    /// </summary>
    public bool? ReuseLastSuccessfulBuild { get; init; }

    /// <summary>
    /// Gets the object name of the TeamCity build type generated for a product build configuration. Declared here
    /// because a dependency on a build configuration and the build configuration itself must agree on the name, and
    /// TeamCity derives the build type identifier from it.
    /// </summary>
    internal static string GetBuildObjectName( BuildConfiguration configuration ) => $"{configuration}Build";

    /// <summary>
    /// Gets the object name of the TeamCity build type this dependency resolves to, or <c>null</c> when the target
    /// does not exist or is not exported. Validation reports those cases; the generator never sees them.
    /// </summary>
    internal string? TryGetObjectName( Product product, IEnumerable<AdditionalCiBuildConfiguration> configurations )
    {
        if ( this.ConfigurationId != null )
        {
            return configurations.Any( c => string.Equals( c.Id, this.ConfigurationId, StringComparison.Ordinal ) )
                ? this.ConfigurationId
                : null;
        }

        var configuration = this.Configuration!.Value;

        return product.Configurations[configuration].ExportsToTeamCityBuild ? GetBuildObjectName( configuration ) : null;
    }

    /// <summary>
    /// Maps this declaration onto the emission model. This is the only place where a dependency on a build
    /// configuration of the same product becomes a <see cref="TeamCitySnapshotDependency"/>, so the two ways of
    /// declaring one cannot drift apart.
    /// </summary>
    internal TeamCitySnapshotDependency ToTeamCitySnapshotDependency(
        string objectName,
        string? defaultArtifactRules,
        bool inheritedReuseLastSuccessfulBuild )
        => new(
            objectName,

            // A build configuration of the same product is addressed by the object name of its build type, never by
            // an absolute TeamCity identifier.
            false,
            this.ArtifactRules switch
            {
                null => defaultArtifactRules,
                { Length: 0 } => null,

                // An escaped newline, not a real one: dependency rules are emitted into a single-line Kotlin string,
                // which a real line break would terminate.
                var rules => string.Join( @"\n", rules )
            },
            ReuseBuilds: this.ReuseLastSuccessfulBuild ?? inheritedReuseLastSuccessfulBuild
                ? ReuseBuilds.LastSuccessful
                : ReuseBuilds.Default,
            CleanDestination: this.CleanDestination );

    public override string ToString() => this.ConfigurationId ?? $"Build [{this.Configuration}]";
}
