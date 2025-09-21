// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System;

namespace PostSharp.Engineering.BuildTools.Build.Model
{
    // ReSharper disable once InconsistentNaming

    /// <summary>
    /// Information about a build, required to format a <see cref="ParametricString"/>.
    /// </summary>
    public record BuildInfo
    {
        internal BuildInfo( string? packageVersion, BuildConfiguration configuration, Product product, string? packagePreviewVersion ) : this(
            packageVersion,
            configuration,
            product.DependencyDefinition,
            packagePreviewVersion ) { }

        internal BuildInfo(
            string? packageVersion,
            BuildConfiguration configuration,
            DependencyDefinition dependencyDefinition,
            string? packagePreviewVersion ) : this(
            packageVersion,
            configuration.ToString(),
            dependencyDefinition.MSBuildConfiguration[configuration],
            packagePreviewVersion ) { }

        internal BuildInfo( string? packageVersion, string configuration, string msBuildConfiguration, string? packagePreviewVersion )
        {
            this.PackageVersion = packageVersion;
            this.Configuration = configuration;
            this.MSBuildConfiguration = msBuildConfiguration;
            this.PackagePreviewVersion = packagePreviewVersion;
        }

        public bool IsPrerelease => this.PackageVersion?.Contains( "-", StringComparison.Ordinal ) ?? throw new InvalidOperationException();

        /// <summary>Full NuGet package version.</summary>
        public string? PackageVersion { get; init; }

        /// <summary>Configuration name.</summary>
        public string Configuration { get; init; }

        /// <summary>MSBuild configuration name.</summary>
        public string MSBuildConfiguration { get; init; }

        public string? PackagePreviewVersion { get; init; }

        public void Deconstruct( out string? PackageVersion, out string Configuration, out string MSBuildConfiguration, out string? PackagePreviewVersion )
        {
            PackageVersion = this.PackageVersion;
            Configuration = this.Configuration;
            MSBuildConfiguration = this.MSBuildConfiguration;
            PackagePreviewVersion = this.PackagePreviewVersion;
        }
    }
}