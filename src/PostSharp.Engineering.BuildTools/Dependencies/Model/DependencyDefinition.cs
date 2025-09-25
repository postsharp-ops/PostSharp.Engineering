// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using System;
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

        // If the product is part of a consolidated build, pre-publishing takes place and the deployment is preformed from the release branch.
        // If not, the deployment is performed from the default branch, and post-publishing is is part of the publishing step.
        public string PublishingBranch => this.ReleaseBranch != null && this.ProductFamily.HasConsolidatedBuild ? this.ReleaseBranch : this.Branch;

        public CiProjectConfiguration CiConfiguration { get; }

        public bool IsVersioned { get; }

        public bool GenerateSnapshotDependency { get; init; } = true;

        public string EngineeringDirectory { get; init; } = "eng";

        public string CodeStyle { get; init; } = "Standard";

        public VcsRepository VcsRepository { get; }

        public string PrivateArtifactsDirectory { get; init; } = Path.Combine( "artifacts", "publish", "private" );

        public string PublicArtifactsDirectory { get; init; } = Path.Combine( "artifacts", "publish", "public" );

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

        public ParametrizedDependency ToDependency() => this.ToDependency( this.DefaultConfigurationMapping );

        public ParametrizedDependency ToDependency( ConfigurationSpecific<BuildConfiguration> configurationMapping )
            => new( this ) { ConfigurationMapping = configurationMapping };

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