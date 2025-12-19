// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System;
using System.Diagnostics.CodeAnalysis;

namespace PostSharp.Engineering.BuildTools.Build.Model
{
    // ReSharper disable once InconsistentNaming

    [Obsolete( "Renamed to BuildArguments." )]
    public record BuildInfo : BuildArguments;

    /// <summary>
    /// Information about a build, required to format a <see cref="ParametricString"/>.
    /// </summary>
    public record BuildArguments
    {
        public BuildArguments() { }

        internal BuildArguments( string? packageVersion, BuildConfiguration configuration, Product product, string? packagePreviewVersion ) : this(
            packageVersion,
            configuration,
            product.DependencyDefinition,
            packagePreviewVersion ) { }

        private BuildArguments(
            string? packageVersion,
            BuildConfiguration configuration,
            DependencyDefinition dependencyDefinition,
            string? packagePreviewVersion ) : this(
            packageVersion,
            configuration.ToString(),
            dependencyDefinition.MSBuildConfiguration[configuration],
            packagePreviewVersion ) { }

        private BuildArguments( string? packageVersion, string configuration, string msBuildConfiguration, string? packagePreviewVersion )
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
        public string? Configuration { get; init; }

        /// <summary>MSBuild configuration name.</summary>
        public string? MSBuildConfiguration { get; init; }

        public string? PackagePreviewVersion { get; init; }

        public static BuildArguments ReadFromArtifactManifest( BuildContext context, BuildConfiguration buildConfiguration )
            => ArtifactManifestFile.CreateParametricStringArguments( context, buildConfiguration );

        [Obsolete( "Renamed TryReadFromAutoUpdatedVersions." )]
        public static bool TryCreate( BuildContext context, BuildConfiguration configuration, [NotNullWhen( true )] out BuildArguments? buildArguments )
            => TryReadFromAutoUpdatedVersionsFile( context, configuration, out buildArguments );

        public static bool TryReadFromAutoUpdatedVersionsFile(
            BuildContext context,
            BuildConfiguration configuration,
            [NotNullWhen( true )] out BuildArguments? buildArguments )
        {
            if ( !MainVersionFile.TryRead( context, out var mainVersionFile ) )
            {
                buildArguments = null;

                return false;
            }

            if ( !AutoUpdatedVersionsFile.TryRead( context, out var packageVersion, out _ ) )
            {
                buildArguments = null;

                return false;
            }

            buildArguments = new BuildArguments()
            {
                MSBuildConfiguration = context.Product.DependencyDefinition.MSBuildConfiguration[configuration],
                Configuration = configuration.ToString(),
                PackageVersion = packageVersion
            };

            return true;
        }

        public static bool TryCreate(
            BuildContext context,
            BuildSettings settings,
            [NotNullWhen( true )] out BuildArguments? buildArguments )
            => TryCreate( context, settings.BuildConfiguration, settings.GetVersionSpec( settings.BuildConfiguration ), settings.UserName, out buildArguments );

        public static bool TryCreate(
            BuildContext context,
            BuildConfiguration configuration,
            VersionSpec versionSpec,
            string? userName,
            [NotNullWhen( true )] out BuildArguments? buildArguments )
        {
            if ( !MainVersionFile.TryRead( context, out var mainVersionFile ) )
            {
                buildArguments = null;

                return false;
            }

            if ( !VersionComponents.TryCompute( context, configuration, mainVersionFile, null, versionSpec, userName, out var versionComponents ) )
            {
                buildArguments = null;

                return false;
            }

            buildArguments = new BuildArguments()
            {
                MSBuildConfiguration = context.Product.DependencyDefinition.MSBuildConfiguration[configuration],
                Configuration = configuration.ToString(),
                PackageVersion = versionComponents.PackageVersion
            };

            return true;
        }
    }
}