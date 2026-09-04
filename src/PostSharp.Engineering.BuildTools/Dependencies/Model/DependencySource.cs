// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using System;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using System.Xml.XPath;

namespace PostSharp.Engineering.BuildTools.Dependencies.Model
{
    public sealed class DependencySource
    {
        /// <summary>
        /// Gets the NuGet version of the dependency, if <see cref="SourceKind"/> is set to <see cref="DependencySourceKind.Feed"/>.
        /// </summary>
        public string? Version { get; private init; }

        public ICiBuildSpec? BuildServerSource { get; internal set; }

        internal string? VersionFile { get; set; }

        public DependencySourceKind SourceKind { get; private init; }

        public DependencyConfigurationOrigin Origin { get; private init; }

        public string? LocalPath { get; private init; }

        public string GetResolvedLocalPath( BuildContext context, string dependencyKey )
        {
            if ( this.SourceKind != DependencySourceKind.Local )
            {
                throw new InvalidOperationException( "The dependency source must be local." );
            }

            var localPath = this.LocalPath == null
                ? Path.Combine(
                    context.RepoDirectory,
                    "..",
                    dependencyKey )
                : Path.Combine( context.RepoDirectory, this.LocalPath );

            return Path.GetFullPath( localPath );
        }

        public static DependencySource CreateLocalDependency( DependencyConfigurationOrigin origin, string? path )
            => new() { Origin = origin, SourceKind = DependencySourceKind.Local, LocalPath = path };

        /// <summary>
        /// Creates a <see cref="DependencySource"/> that represents a build server artifact dependency that has been restored,
        /// and that exists under the 'dependencies' directory. Uses the consumer-side <see cref="ParametrizedDependency.Key"/>
        /// for the path and the property prefix, so this method is alias-aware.
        /// </summary>
        public static DependencySource CreateRestoredDependency(
            BuildContext context,
            ParametrizedDependency dependency,
            DependencyConfigurationOrigin origin )
        {
            // For aliased deps in CI mode, TeamCity restores the producer's {Name}.version.props to dependencies/{Key}/.
            // The on-disk file is {Name}.version.props (producer's name) but Core expects {Key}.version.props (alias).
            // The full fetch path runs this transform via DependenciesHelper.ResolveRestoredDependencies, but VersionFile.TryRead
            // (called from PrepareCommand) reaches Core directly without going through fetch. Run the transform here so the
            // CI prepare step doesn't crash before the artifact-aware code path even gets a chance.
            if ( dependency.Alias != null )
            {
                var aliasDirectory = TeamCityHelper.GetRestoredDependencyDirectory( context.RepoDirectory, dependency.Key );
                var producerVersionPropsPath = Path.Combine( aliasDirectory, dependency.Definition.Name + ".version.props" );
                var aliasedVersionPropsPath = Path.Combine( aliasDirectory, dependency.Key + ".version.props" );

                if ( File.Exists( producerVersionPropsPath ) )
                {
                    DependenciesHelper.TransformVersionPropsForAlias(
                        producerVersionPropsPath,
                        aliasedVersionPropsPath,
                        dependency.Definition.NameWithoutDot,
                        dependency.KeyWithoutDot );
                }
            }

            return CreateRestoredDependencyCore( context, dependency.Key, dependency.KeyWithoutDot, origin );
        }

        /// <summary>
        /// Creates a <see cref="DependencySource"/> that represents a build server artifact dependency that has been restored,
        /// and that exists under the 'dependencies' directory. Uses <see cref="DependencyDefinition.Name"/> as the key, so this
        /// overload is suitable only for unaliased references. Prefer the <see cref="ParametrizedDependency"/> overload at use sites
        /// that may involve aliases.
        /// </summary>
        public static DependencySource CreateRestoredDependency(
            BuildContext context,
            DependencyDefinition dependencyDefinition,
            DependencyConfigurationOrigin origin )
            => CreateRestoredDependencyCore( context, dependencyDefinition.Name, dependencyDefinition.NameWithoutDot, origin );

        private static DependencySource CreateRestoredDependencyCore(
            BuildContext context,
            string key,
            string keyWithoutDot,
            DependencyConfigurationOrigin origin )
        {
            var path = TeamCityHelper.GetRestoredDependencyVersionFile( context.RepoDirectory, key );

            // The version file exists only after the dependency has been restored as a build server artifact. A command
            // that does not consume the dependency itself can legitimately run in a checkout that has no 'dependencies'
            // directory. Returning a source with no build identifier lets the callers report that the dependencies have
            // not been fetched, which is the diagnostic they already implement, instead of failing on a missing file.
            if ( !File.Exists( path ) )
            {
                return CreateRestoredDependency( null, origin );
            }

            var document = XDocument.Load( path );

            var buildNumber = document.Root!.XPathSelectElement( $"/Project/PropertyGroup/{keyWithoutDot}BuildNumber" )?.Value;
            var buildType = document.Root!.XPathSelectElement( $"/Project/PropertyGroup/{keyWithoutDot}BuildType" )?.Value;

            CiBuildId? buildId;

            if ( string.IsNullOrEmpty( buildNumber ) || string.IsNullOrEmpty( buildType ) )
            {
                buildId = null;
            }
            else
            {
                buildId = new CiBuildId( int.Parse( buildNumber, CultureInfo.InvariantCulture ), buildType );
            }

            return CreateRestoredDependency( buildId, origin );
        }

        public static DependencySource CreateRestoredDependency( CiBuildId? buildId, DependencyConfigurationOrigin origin )
            => new() { Origin = origin, SourceKind = DependencySourceKind.RestoredDependency, BuildServerSource = buildId };

        public static DependencySource CreateFeed( string? version, DependencyConfigurationOrigin origin )
            => new() { Origin = origin, SourceKind = DependencySourceKind.Feed, Version = version };

        public static DependencySource CreateBuildServerSource( ICiBuildSpec source, DependencyConfigurationOrigin origin )
            => new() { Origin = origin, SourceKind = DependencySourceKind.BuildServer, BuildServerSource = source };

        public override string ToString()
        {
            switch ( this.SourceKind )
            {
                case DependencySourceKind.BuildServer or DependencySourceKind.RestoredDependency:
                    return $"{this.SourceKind}, {this.BuildServerSource}, Origin={this.Origin}";

                case DependencySourceKind.Local:
                    {
                        return $"{this.SourceKind}, Origin={this.Origin}";
                    }

                case DependencySourceKind.Feed:
                    return $"{this.SourceKind}, {this.Version}, Origin={this.Origin}";

                default:
                    return "<Error>";
            }
        }
    }
}