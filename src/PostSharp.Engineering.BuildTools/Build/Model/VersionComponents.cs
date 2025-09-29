// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Build.Evaluation;
using NuGet.Versioning;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Model;

internal record VersionComponents
{
    private VersionComponents(
        string mainVersion,
        string versionPrefix,
        int patchNumber,
        string versionSuffix,
        BuildConfiguration configuration,
        Product product )
    {
        this.MainVersion = mainVersion;
        this.VersionPrefix = versionPrefix;
        this.PatchNumber = patchNumber;
        this.VersionSuffix = versionSuffix;

        this.PreviewVersionSuffix = configuration == BuildConfiguration.Public ? "preview" : this.VersionSuffix;
        this.PackageVersionWithoutSuffix = this.PatchNumber == 0 ? this.VersionPrefix : this.VersionPrefix + "." + this.PatchNumber;

        // Metalama.Compiler, because of Arcade, requires the version number to be decomposed in a prefix, patch number, and suffix.
        // In Arcade, the package naming scheme is different because the patch number is not a part of the package name.

        if ( product.GenerateArcadeProperties )
        {
            var arcadeSuffix = string.IsNullOrEmpty( this.VersionSuffix ) ? "" : this.VersionSuffix;
            var previewArcadeSuffix = this.PreviewVersionSuffix;

            void AppendToArcadeSuffix( string s )
            {
                arcadeSuffix += s;
                previewArcadeSuffix += s;
            }

            if ( this.PatchNumber > 0 )
            {
                if ( arcadeSuffix.Length > 0 )
                {
                    AppendToArcadeSuffix( "-" );
                }
                else
                {
                    // It should not happen that we have a patch number without a suffix.
                    AppendToArcadeSuffix( "-patch-" + configuration );
                }

                AppendToArcadeSuffix( this.PatchNumber.ToString( CultureInfo.InvariantCulture ) );
            }

            var packageSuffixWithDash = string.IsNullOrEmpty( arcadeSuffix ) ? "" : "-" + arcadeSuffix;
            this.PackageVersion = this.VersionPrefix + packageSuffixWithDash;
            this.PackagePreviewVersion = this.VersionPrefix + "-" + previewArcadeSuffix;
            this.ArcadeSuffix = arcadeSuffix;
        }
        else
        {
            var packageSuffix = string.IsNullOrEmpty( this.VersionSuffix ) ? "" : "-" + this.VersionSuffix;
            this.PackageVersion = this.PackageVersionWithoutSuffix + packageSuffix;
            this.PackagePreviewVersion = this.PackageVersionWithoutSuffix + "-" + this.PreviewVersionSuffix;
        }
    }

    public string AssemblyVersion => this.VersionPrefix + "." + this.PatchNumber;

    public string PackageVersionWithoutSuffix { get; set; }

    public string PreviewVersionSuffix { get; set; }

    public string? ArcadeSuffix { get; }

    public string PackagePreviewVersion { get; }

    public string PackageVersion { get; }

    public string MainVersion { get; }

    public string VersionPrefix { get; }

    public int PatchNumber { get; }

    public string VersionSuffix { get; }

    public static bool TryCompute(
        BuildContext context,
        BuildSettings settings,
        BuildConfiguration configuration,
        MainVersionFile mainVersionFile,
        DependenciesConfigurationFile dependenciesConfigurationFile,
        [NotNullWhen( true )] out VersionComponents? version )
    {
        var product = context.Product;
        var configurationLowerCase = configuration.ToString().ToLowerInvariant();

        version = null;
        string? mainVersion = null;

        if ( product.MainVersionDependency != null )
        {
            var mainVersionDependencyName = product.MainVersionDependency.Name;

            // The main version is defined in a dependency. Load the import file.

            if ( !dependenciesConfigurationFile.Dependencies.TryGetValue( mainVersionDependencyName, out var dependencySource ) )
            {
                context.Console.WriteError( $"Cannot find a dependency named '{mainVersionDependencyName}'." );

                return false;
            }

            // Note that the version suffix is not copied from the dependency, only the main version. 

            if ( dependencySource.VersionFile == null )
            {
                if ( !VersionFile.TryRead( context, settings, out var localVersionFile ) )
                {
                    return false;
                }

                if ( !localVersionFile.Dependencies.TryGetValue( product.MainVersionDependency.Name, out var mainDependencySource ) )
                {
                    context.Console.WriteError( $"Version file doesn't contain version for {product.MainVersionDependency.Name}." );

                    return false;
                }

                var versionString = mainDependencySource.Version;

                if ( !NuGetVersion.TryParse( versionString, out var mainFullVersion ) )
                {
                    context.Console.WriteError( $"Could not parse the version '{versionString}'." );

                    return false;
                }

                mainVersion = new NuGetVersion( mainFullVersion.Major, mainFullVersion.Minor, mainFullVersion.Patch ).ToString();
            }
            else
            {
                var versionFile = Project.FromFile( dependencySource.VersionFile, MSBuildLoadOptions.IgnoreImportErrors );

                var propertyName = product.MainVersionDependency!.NameWithoutDot + "MainVersion";

                mainVersion = versionFile.Properties.SingleOrDefault( p => p.Name == propertyName )
                    ?.UnevaluatedValue;

                if ( string.IsNullOrEmpty( mainVersion ) )
                {
                    context.Console.WriteError( $"The file '{dependencySource.VersionFile}' does not contain the {propertyName}." );

                    return false;
                }

                ProjectCollection.GlobalProjectCollection.UnloadAllProjects();
            }
        }

        if ( !string.IsNullOrEmpty( mainVersionFile.OverriddenPatchVersion )
             && !mainVersionFile.OverriddenPatchVersion.StartsWith( mainVersion ?? mainVersionFile.MainVersion + ".", StringComparison.Ordinal ) )
        {
            context.Console.WriteError(
                $"The OverriddenPatchVersion property in MainVersion.props ({mainVersionFile.OverriddenPatchVersion}) does not match the MainVersion property value ({mainVersion ?? mainVersionFile.MainVersion})." );

            return false;
        }

        var versionPrefix = mainVersion ?? mainVersionFile.MainVersion;
        string versionSuffix;
        int patchNumber;

        var versionSpec = settings.GetVersionSpec( configuration );
        var versionSpecKind = versionSpec.Kind;

        if ( configuration == BuildConfiguration.Public )
        {
            versionSpecKind = VersionKind.Public;
        }

        switch ( versionSpecKind )
        {
            case VersionKind.Local:
                {
                    // Local build with timestamp-based version and randomized package number. For the assembly version we use a local incremental file stored in the user profile.

                    var localVersionDirectory = PathHelper.GetEngineeringDataDirectory();

                    var localVersionFile = Path.Combine( localVersionDirectory, $"{product.ProductName}.version" );
                    int localVersion;

                    if ( File.Exists( localVersionFile ) )
                    {
                        localVersion = int.Parse(
                            File.ReadAllText( localVersionFile ),
                            CultureInfo.InvariantCulture ) + 1;
                    }
                    else
                    {
                        localVersion = 1;
                    }

                    if ( localVersion < 1000 )
                    {
                        localVersion = 1000;
                    }

                    if ( !Directory.Exists( localVersionDirectory ) )
                    {
                        Directory.CreateDirectory( localVersionDirectory );
                    }

                    File.WriteAllText( localVersionFile, localVersion.ToString( CultureInfo.InvariantCulture ) );

                    var userName = settings.UserName;
                    versionSuffix = $"local-{userName}-{configurationLowerCase}";

                    patchNumber = localVersion;

                    break;
                }

            case VersionKind.Numbered:
                {
                    // Build server build with a build number given by the build server
                    patchNumber = versionSpec.Number;
                    versionSuffix = $"dev-{configurationLowerCase}";

                    break;
                }

            case VersionKind.Public:
                // Public build
                versionSuffix = mainVersionFile.PackageVersionSuffix.TrimStart( '-' );
                patchNumber = 0;

                if ( !string.IsNullOrWhiteSpace( mainVersionFile.OverriddenPatchVersion ) )
                {
                    var parsedOverriddenPatchedVersion = Version.Parse( mainVersionFile.OverriddenPatchVersion );
                    patchNumber = parsedOverriddenPatchedVersion.Revision;
                }

                break;

            default:
                throw new InvalidOperationException();
        }

        version = new VersionComponents( mainVersion ?? mainVersionFile.MainVersion, versionPrefix, patchNumber, versionSuffix, configuration, product );

        return true;
    }
}