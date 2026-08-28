// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;

namespace PostSharp.Engineering.BuildTools.Build.Files;

/// <summary>
/// Represents the information of <c>Versions.props</c> and <c>Directory.Packages.props</c>.
/// </summary>
public class VersionFile
{
    private VersionFile( ImmutableDictionary<string, DependencySource> dependencies )
    {
        this.Dependencies = dependencies;
    }

    public ImmutableDictionary<string, DependencySource> Dependencies { get; }

    public static bool TryRead(
        BuildContext context,
        CommonCommandSettings settings,
        [NotNullWhen( true )] out VersionFile? versionFile )
    {
        versionFile = null;
        var dependenciesBuilder = ImmutableDictionary.CreateBuilder<string, DependencySource>();
        var versionsPropsPath = Path.Combine( context.RepoDirectory, context.Product.VersionsFilePath );
        var directoryPackagesPropsPath = Path.Combine( context.RepoDirectory, "Directory.Packages.props" );

        if ( !File.Exists( versionsPropsPath ) )
        {
            context.Console.WriteError( $"The file '{versionsPropsPath}' does not exist." );

            return false;
        }

        var projectOptions = new ProjectOptions { GlobalProperties = new Dictionary<string, string>() { ["DoNotLoadGeneratedVersionFiles"] = "True" } };

        var versionsProject = Project.FromFile( versionsPropsPath, projectOptions );
        Project? centralPackageManagementVersionsProject = null;

        if ( File.Exists( directoryPackagesPropsPath ) )
        {
            centralPackageManagementVersionsProject = Project.FromFile( directoryPackagesPropsPath, projectOptions );
        }

        var defaultDependencyProperties = context.Product.ParametrizedDependencies
            .ToDictionary<ParametrizedDependency, string, (string Version, string File)>(
                d => d.Key,
                d =>
                {
                    var property = versionsProject.Properties.SingleOrDefault( p => p.Name == d.KeyWithoutDot + "Version" );

                    if ( property == null )
                    {
                        property = centralPackageManagementVersionsProject?.Properties.SingleOrDefault( p => p.Name == d.KeyWithoutDot + "Version" );
                    }

                    if ( property == null )
                    {
                        return default;
                    }

                    var s = property.EvaluatedValue.Trim();

                    // A property that comes from the environment or from the command line has no backing XML, so
                    // Xml is null and only the value is available. This is a legitimate way to override a dependency
                    // version, and dereferencing the location would fail the command with a NullReferenceException
                    // that names neither the property nor the cause.
                    return (s, property.Xml?.Location.File ?? "the environment or the command line");
                } );

        ProjectCollection.GlobalProjectCollection.UnloadAllProjects();

        foreach ( var dependencyDefinition in context.Product.ParametrizedDependencies )
        {
            var dependencyVersion = defaultDependencyProperties[dependencyDefinition.Key];

            if ( dependencyVersion == default )
            {
                // A property is required because we update it during the release process.

                context.Console.WriteError(
                    $"A property named '{dependencyDefinition.KeyWithoutDot}Version' must be defined, typically in 'eng/AutoUpdatedVersions.props', even with empty value." );

                continue;
            }

            // The property value can be either empty or a semantic version, but empty values are not allowed on guest devices,
            // i.e. for build outside our VPN.

            if ( dependencyVersion.Version != "" && !Regex.IsMatch( dependencyVersion.Version, @"^\d+.*$" ) )
            {
                context.Console.WriteError(
                    $"{dependencyVersion.File}: invalid value '{dependencyVersion}' for property '{dependencyDefinition.Key}Version': the value is neither empty nor a valid version number." );

                versionFile = null;

                return false;
            }

            // Set the default source of the dependency according to the build context.
            DependencySource dependencySource;

            if ( BuildContext.IsGuestDevice || !dependencyDefinition.Definition.GenerateSnapshotDependency )
            {
                if ( dependencyVersion.Version == "" )
                {
                    context.Console.WriteError( $"{dependencyVersion.File}: missing value for property '{dependencyDefinition.KeyWithoutDot}Version'." );

                    versionFile = null;

                    return false;
                }

                dependencySource = DependencySource.CreateFeed( dependencyVersion.Version, DependencyConfigurationOrigin.Default );
            }
            else if ( settings.UseLocalDependencies && dependencyDefinition.Definition.ProductFamily == context.Product.ProductFamily )
            {
                dependencySource = DependencySource.CreateLocalDependency( DependencyConfigurationOrigin.Default, null );
            }
            else if ( context.IsContinuousIntegrationBuild )
            {
                dependencySource = DependencySource.CreateRestoredDependency(
                    context,
                    dependencyDefinition,
                    DependencyConfigurationOrigin.Default );
            }
            else
            {
                // The branch stored here cannot depend on the build configuration, which is not known at this point.
                // ResolveBuildNumbersFromBranches substitutes the publishing branch where it applies.
                dependencySource = DependencySource.CreateBuildServerSource(
                    new CiLatestBuildOfBranch( dependencyDefinition.Definition.Branch ),
                    DependencyConfigurationOrigin.Default );
            }

            dependenciesBuilder[dependencyDefinition.Key] = dependencySource;
        }

        versionFile = new VersionFile( dependenciesBuilder.ToImmutable() );

        return true;
    }

    internal static bool Validate( BuildContext context, DependenciesConfigurationFile dependenciesConfigurationFile )
    {
        var versionsPath = Path.Combine( context.RepoDirectory, context.Product.VersionsFilePath );
        var document = XDocument.Load( versionsPath );
        var hasError = false;

        foreach ( var dependency in dependenciesConfigurationFile.Dependencies.Keys )
        {
            // Look up the parametrized dependency to get the consumer-side key (which equals Name when no alias is set).
            var keyWithoutDot = context.Product.TryGetDependency( dependency, out var parametrizedDependency )
                ? parametrizedDependency.KeyWithoutDot
                : context.Product.ProductFamily.GetDependencyDefinition( dependency ).NameWithoutDot;

            var propertyName = $"{keyWithoutDot}Version";

            var elements = document.Root!.XPathSelectElements( $"/Project/PropertyGroup/{propertyName}" ).ToList();

            switch ( elements.Count )
            {
                case > 1:
                    context.Console.WriteError( $"{versionsPath}: the file contains more than one definition of the '{propertyName}' property." );
                    hasError = true;

                    break;

                case 1 when elements[0].HasAttributes:
                    context.Console.WriteError( $"{versionsPath}: the '{propertyName}' property definition should not have any attribute." );
                    hasError = true;

                    break;
            }
        }

        return !hasError;
    }
}