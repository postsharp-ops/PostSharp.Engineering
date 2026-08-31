// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Dependencies.Model
{
    /// <summary>
    /// Represents the definition of a dependency. Dependencies are typically defined in PostSharp.Engineering. Dependency definitions
    /// must not define any property that depends on the referencing product. Any such property must be defined in <see cref="ParametrizedDependency"/>.
    /// </summary>
    [PublicAPI]
    public class DependencyDefinition
    {
        private readonly string[]? _packagePatterns;

        [return: NotNullIfNotNull( "dependency" )]
        public static implicit operator DependencyDefinition?( ParametrizedDependency? dependency ) => dependency?.Definition;

        public ProductFamily ProductFamily { get; }

        public string Name { get; }

        public string NameWithoutDot => this.Name.Replace( ".", "", StringComparison.Ordinal );

        /// <summary>
        /// Gets the development branch for this product.
        /// </summary>
        /// <remarks>
        /// The development branch is the target branch for all topic and feature branches. 
        /// </remarks>
        public string Branch { get; }

        /// <summary>
        /// Gets the release branch for this product.
        /// </summary>
        /// <remarks>
        /// The release branch is the branch containing public code of the latest published version of the product.
        /// The release branch is not set for products not having their source code published.
        /// </remarks>
        public string? ReleaseBranch { get; }

        /// <summary>
        /// Forces <see cref="PublishingBranch"/> to resolve to <see cref="ReleaseBranch"/> for products that publish from
        /// the release branch even though they are not part of a consolidated build (e.g. Metalama.Vsx).
        /// </summary>
        public bool PublishesFromReleaseBranch { get; init; }

        // If the product is part of a consolidated build, pre-publishing takes place and the deployment is performed from the release branch.
        // If not, the deployment is performed from the default branch, and post-publishing is part of the publishing step.
        // Products with PublishesFromReleaseBranch = true also publish from the release branch even without a consolidated build.
        public string PublishingBranch
            => this.ReleaseBranch != null && (this.ProductFamily.HasConsolidatedProduct || this.PublishesFromReleaseBranch)
                ? this.ReleaseBranch
                : this.Branch;

        public CiProjectConfiguration CiConfiguration { get; }

        public bool IsVersioned { get; }

        public bool GenerateSnapshotDependency { get; init; } = true;

        public bool IsConsolidated { get; init; }

        public string EngineeringDirectory { get; init; } = "eng";

        /// <summary>
        /// Gets or set the list of other directories (additionally to <see cref="EngineeringDirectory"/>) that should
        /// be checked out when <see cref="SourceDependenciesRequirements.EngOnly"/> is specified. 
        /// </summary>
        public string[] AdditionalEngineeringDirectories { get; init; } = [];

        public string CodeStyle { get; init; } = "Standard";

        public VcsRepository VcsRepository { get; }

        /// <summary>
        /// Gets the identifier of the TeamCity GitHub App connection that issues the build-scoped token of this
        /// repository, when the repository does not belong to the GitHub organization of
        /// <see cref="Model.ProductFamily.GitHubAppConnectionId"/>. When <c>null</c>, the value of the family is used.
        /// </summary>
        public string? GitHubAppConnectionId { get; init; }

        /// <summary>
        /// Gets the identifier of the TeamCity GitHub App connection that issues the build-scoped token of this
        /// repository, or <c>null</c> when no connection is configured.
        /// </summary>
        public string? EffectiveGitHubAppConnectionId => this.GitHubAppConnectionId ?? this.ProductFamily.GitHubAppConnectionId;

        public ParametrizedDependency[] Dependencies { get; init; } = [];

        private ImmutableDictionary<string, ParametrizedDependency>? _aliasedTransitiveDependencies;

        /// <summary>
        /// Gets the references to the transitive dependencies that inherit an alias from an aliased direct dependency,
        /// indexed by <see cref="ParametrizedDependency.Key"/>. The dictionary is empty when no direct dependency is aliased.
        /// </summary>
        /// <remarks>
        /// These references have no declaration at the consumer's use site, so they are not in <see cref="Dependencies"/>,
        /// but their key is still needed to resolve them by name. See
        /// <see cref="ParametrizedDependency.GetAliasForTransitiveDependency"/> for the rule that derives the alias.
        /// </remarks>
        public IReadOnlyDictionary<string, ParametrizedDependency> AliasedTransitiveDependencies
            => this._aliasedTransitiveDependencies ??= this.GetAliasedTransitiveDependencies();

        private ImmutableDictionary<string, ParametrizedDependency> GetAliasedTransitiveDependencies()
        {
            var builder = ImmutableDictionary.CreateBuilder<string, ParametrizedDependency>( StringComparer.Ordinal );

            foreach ( var directDependency in this.Dependencies )
            {
                if ( directDependency.Alias != null )
                {
                    PopulateRecursive( directDependency );
                }
            }

            return builder.ToImmutable();

            void PopulateRecursive( ParametrizedDependency parent )
            {
                foreach ( var child in parent.Definition.Dependencies )
                {
                    var alias = parent.GetAliasForTransitiveDependency( child.Definition );

                    if ( alias == null )
                    {
                        continue;
                    }

                    var aliasedChild = child with { Alias = alias };

                    // TryAdd also terminates the recursion on a cycle or on a diamond in the dependency graph.
                    if ( !builder.TryAdd( alias, aliasedChild ) )
                    {
                        continue;
                    }

                    PopulateRecursive( aliasedChild );
                }
            }
        }

        public IReadOnlySet<DependencyConfiguration> GetAllDependencies( BuildConfiguration buildConfiguration )
        {
            HashSet<DependencyConfiguration> dependencies = new();
            PopulateRecursive( parent: null, this, buildConfiguration, ancestorIsLastSuccessful: false );

            return dependencies;

            void PopulateRecursive(
                ParametrizedDependency? parent,
                DependencyDefinition dependency,
                BuildConfiguration configuration,
                bool ancestorIsLastSuccessful )
            {
                foreach ( var child in dependency.Dependencies )
                {
                    var childConfiguration = child.ConfigurationMapping[configuration];

                    // Propagate LastSuccessful through transitive deps: if any ancestor on the path from the consumer to
                    // this child is LastSuccessful, we don't trigger that ancestor's build, so chaining the build of its
                    // transitive deps is pointless. Treat the whole subtree under a LastSuccessful node as LastSuccessful.
                    var childIsLastSuccessful = ancestorIsLastSuccessful || child.ArtifactPickup == DependencyArtifactPickup.LastSuccessful;

                    var effectivePickup = childIsLastSuccessful
                        ? DependencyArtifactPickup.LastSuccessful
                        : child.ArtifactPickup;

                    // A transitive dependency reached through an aliased dependency inherits that alias, so that the
                    // artifacts of the two versions of the same product are unpacked into two different directories.
                    // parent is null for the direct dependencies of the consumer, which keep the reference as declared.
                    var alias = parent?.GetAliasForTransitiveDependency( child.Definition );
                    var effectiveChild = alias == null ? child : child with { Alias = alias };

                    var dependencyConfiguration = new DependencyConfiguration( child, childConfiguration )
                    {
                        Parametrized = effectiveChild, EffectiveArtifactPickup = effectivePickup
                    };

                    if ( !dependencies.Add( dependencyConfiguration ) )
                    {
                        continue;
                    }

                    PopulateRecursive( effectiveChild, child, childConfiguration, childIsLastSuccessful );
                }
            }
        }

        public DependencyDefinition[] SourceDependencies { get; init; } = [];

        public string PrivateArtifactsDirectory
        {
            [Obsolete( "Use GetPrivateArtifactsDirectory." )]
            get;
            init;
        } = Path.Combine( "artifacts", "publish", "private" );

        [Obsolete( "This property should not be used except for Metalama.Compiler, for historical reasons. Use PrivateArtifactsDirectory." )]
        public ParametricString ParametricPrivateArtifactsDirectory { get; init; } = Path.Combine( "artifacts", "publish", "private" );

        // Metalama.Compiler uses a placeholder for the MSBuild configuration.
        public string GetPrivateArtifactsDirectory( BuildConfiguration configuration )
#pragma warning disable CS0618 // Type or member is obsolete
            => this.ParametricPrivateArtifactsDirectory.ToString( new BuildArguments() { MSBuildConfiguration = this.MSBuildConfiguration[configuration] } );
#pragma warning restore CS0618 // Type or member is obsolete

        public string PublicArtifactsDirectory { get; init; } = Path.Combine( "artifacts", "publish", "public" );

        /// <summary>
        /// Gets or sets the list of packages produced by this dependency. This list is used to configure package source mapping.
        /// The <c>*</c> wildcard is allowed.
        /// By default, the list is set to <c>MyProduct, MyProduct.*</c>.
        /// </summary>
        public string[] PackagePatterns
        {
            get => this._packagePatterns ?? [this.Name, this.Name + ".*"];
            init => this._packagePatterns = value;
        }

        /// <summary>
        /// Gets or sets the order in which products in the same family should be built. This is a poorman version of a recursive build
        /// taking dependencies into account, because PostSharp.Engineering does not know detailed dependencies.
        /// </summary>
        public int? BuildOrder { get; set; }

        // ReSharper disable once InconsistentNaming
        public ConfigurationSpecific<string> MSBuildConfiguration { get; init; } = new( "Debug", "Release", "Release" );

        /// <summary>
        /// Gets or sets the mapping between the build configuration of the referencing repo and the build configuration of the current repo.
        /// This can be overwritten by the referencing repo using <see cref="ParametrizedDependency.ConfigurationMapping"/>.
        /// Normally, choosing the configuration mapping is the concern of the referencing project and not the dependency definition,
        /// but there is an exception for PostSharp.Engineering and therefore this property is needed.
        /// </summary>
        public ConfigurationSpecific<BuildConfiguration> DefaultConfigurationMapping { get; init; } = new(
            BuildConfiguration.Debug,
            BuildConfiguration.Release,
            BuildConfiguration.Public );

        public bool ExcludeFromRecursiveBuild { get; init; }

        public bool AutoUpdateVersion { get; init; } = true;

        public ParametrizedDependency ToDependency() => this.ToDependency( this.DefaultConfigurationMapping );

        public ParametrizedDependency ToDependency( ConfigurationSpecific<BuildConfiguration> configurationMapping )
            => new( this ) { ConfigurationMapping = configurationMapping };

        /// <summary>
        /// Returns a <see cref="ParametrizedDependency"/> that references this definition under a consumer-side <paramref name="alias"/>.
        /// </summary>
        public ParametrizedDependency WithAlias( string alias ) => this.ToDependency() with { Alias = alias };

        public DependencyDefinition(
            ProductFamily productFamily,
            string dependencyName,
            string branch,
            string? releaseBranch,
            VcsRepository vcsRepository,
            CiProjectConfiguration ciProjectConfiguration,
            bool isVersioned = true )
        {
            this.ProductFamily = productFamily;

            this.Name = dependencyName;
            this.VcsRepository = vcsRepository;
            this.Branch = branch;
            this.ReleaseBranch = releaseBranch;
            this.CiConfiguration = ciProjectConfiguration;
            this.IsVersioned = isVersioned;

            productFamily.Register( this );
        }

        public override string ToString() => this.Name;
    }
}