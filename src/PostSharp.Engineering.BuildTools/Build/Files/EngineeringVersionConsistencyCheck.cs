// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Newtonsoft.Json.Linq;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build.Files;

/// <summary>
/// Warns when the version of PostSharp.Engineering that is about to be written into <c>Versions.{Configuration}.g.props</c>
/// disagrees with the other places that name a version of PostSharp.Engineering.
/// </summary>
/// <remarks>
/// A mismatch here fails silently and is mis-signposted, which is why it deserves a check of its own. The generated file assigns
/// <c>PostSharpEngineeringVersion</c> unconditionally and is imported before <c>Directory.Packages.props</c>, so it alone governs
/// the version of the package that compiles the product definition. Every build then succeeds against the older package, and the
/// mismatch only surfaces when the product definition uses an API added in the newer version -- as a missing member of a type
/// that does exist, which reads as a namespace mistake rather than as a stale package.
/// </remarks>
internal static class EngineeringVersionConsistencyCheck
{
    public static void Verify( BuildContext context, DependenciesConfigurationFile dependenciesConfigurationFile )
    {
        var dependencyName = DevelopmentDependencies.PostSharpEngineering.Name;

        if ( !dependenciesConfigurationFile.Dependencies.TryGetValue( dependencyName, out var dependencySource )
             || dependencySource.SourceKind != DependencySourceKind.Feed
             || string.IsNullOrEmpty( dependencySource.Version ) )
        {
            // Either this product does not consume PostSharp.Engineering, or it consumes a local or build server build,
            // in which case the versions compared below are legitimately unrelated.
            return;
        }

        if ( dependencySource.Origin != DependencyConfigurationOrigin.Default )
        {
            // The version was set explicitly with the 'dependencies set' command, so it is meant to differ from the default.
            return;
        }

        var console = context.Console;
        var pinnedVersion = dependencySource.Version;

        // global.json pins the MSBuild SDK, which ships from the same package as the build tools and is expected to match.
        var globalJsonPath = Path.Combine( context.RepoDirectory, "global.json" );

        if ( File.Exists( globalJsonPath ) )
        {
            string? sdkVersion;

            try
            {
                sdkVersion = JObject.Parse( File.ReadAllText( globalJsonPath ) )["msbuild-sdks"]?["PostSharp.Engineering.Sdk"]
                    ?.Value<string>()
                    ?.Trim();
            }
            catch ( Exception e )
            {
                console.WriteWarning( $"Cannot read '{globalJsonPath}': {e.Message}" );

                sdkVersion = null;
            }

            if ( !string.IsNullOrEmpty( sdkVersion ) && sdkVersion != pinnedVersion )
            {
                console.WriteWarning(
                    $"'{globalJsonPath}' pins PostSharp.Engineering.Sdk {sdkVersion}, but PostSharp.Engineering resolves to {pinnedVersion}, "
                    + $"which is the value being written to '{dependenciesConfigurationFile.FilePath}'. The generated file governs the "
                    + $"PostSharpEngineeringVersion property because it is imported first, so editing global.json alone has no effect. "
                    + $"Run './Build.ps1 dependencies update-eng' to update every source consistently." );
            }
        }

        // The running tool is not necessarily the version it is about to pin. This is the state produced by building the product
        // definition with an overridden PostSharpEngineeringVersion, and the override is lost as soon as the definition is rebuilt.
        var runningVersion = VersionHelper.EngineeringVersion;

        if ( !string.IsNullOrEmpty( runningVersion ) && runningVersion != pinnedVersion )
        {
            console.WriteWarning(
                $"The running PostSharp.Engineering is version {runningVersion}, but PostSharp.Engineering resolves to {pinnedVersion}, "
                + $"which is the value being written to '{dependenciesConfigurationFile.FilePath}'. The next build of the product "
                + $"definition will restore {pinnedVersion}." );
        }
    }
}
