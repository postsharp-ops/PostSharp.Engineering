// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Testing;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

[UsedImplicitly]
internal class GenerateScriptsCommand : BaseCommand<CommonCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, CommonCommandSettings settings ) => Execute( context, settings );

    public static bool Execute( BuildContext context, CommonCommandSettings settings )
    {
        var product = context.Product;

        // The build configurations of the test agents are planned from the test applications, which only an evaluation of
        // the projects tells. They are created before the TeamCity settings, which contain them, and are validated with
        // the configurations that the product declares.
        ImmutableArray<AdditionalCiBuildConfiguration> generatedConfigurations = [];

        if ( product.TestAgents.Length > 0 )
        {
            if ( !product.PublishesTestArchives )
            {
                context.Console.WriteError( "The product declares TestAgents but no solution sets ContainsTestApplications, so no build writes the archives they run." );

                return false;
            }

            // The projects are evaluated in each configuration whose archives the test agents run, because a target
            // framework, an assembly name or a skip reason can depend on it.
            var sourceConfigurations = product.TestArchivesConfigurations;

            if ( sourceConfigurations.IsEmpty )
            {
                context.Console.WriteError( "The product declares TestAgents but no build configuration sets RunsTestArchives, so they have no archives to run." );

                return false;
            }

            // The cells of each configuration are planned from the applications discovered in that configuration, whose skip
            // reasons, platforms, tags and artifacts can differ.
            var applicationsByConfiguration = new Dictionary<BuildConfiguration, IReadOnlyList<TestApplication>>();
            IReadOnlyList<TestApplication>? applications = null;

            foreach ( var sourceConfiguration in sourceConfigurations )
            {
                if ( !TestApplicationDiscovery.TryDiscover( context, sourceConfiguration, out var sourceApplications ) )
                {
                    return false;
                }

                // The list of the archives, which 'Build.ps1 build' checks, is shared by every source, so they must write the
                // same archives.
                if ( applications != null
                     && !applications.Select( a => a.ArchiveName )
                         .Order( StringComparer.Ordinal )
                         .SequenceEqual( sourceApplications.Select( a => a.ArchiveName ).Order( StringComparer.Ordinal ), StringComparer.Ordinal ) )
                {
                    context.Console.WriteError(
                        $"The builds that publish the test archives do not write the same archives: the '{sourceConfiguration}' build configuration "
                        + $"writes a different set than the '{sourceConfigurations[0]}' one. The list of the archives is shared by all of them." );

                    return false;
                }

                applications ??= sourceApplications;
                applicationsByConfiguration.Add( sourceConfiguration, sourceApplications );
            }

            // The packages that each application downloads from the builds that publish them are read from the restore graph of
            // its project. The graph does not depend on the configuration, so the projects are restored once, in the
            // configuration that the repository was prepared in, whose packages exist.
            var restoreSettings = new BuildSettings();
            restoreSettings.Initialize( context );

            foreach ( var solution in product.Solutions.Where( s => s.ContainsTestApplications ) )
            {
                if ( !solution.Restore( context, restoreSettings ) )
                {
                    context.Console.WriteError( $"Cannot restore '{solution.Name}', whose restore graph gives the packages of its test applications. Build the product first." );

                    return false;
                }
            }

            if ( !TestArchivePackages.Sources.TryLoad( context, out var sources ) )
            {
                return false;
            }

            var packagesByArchive = new Dictionary<string, ImmutableArray<string>>( StringComparer.OrdinalIgnoreCase );

            foreach ( var application in applications! )
            {
                if ( !TestArchivePackages.TryGetPackages( context.Console, sources, application, out var packages ) )
                {
                    return false;
                }

                packagesByArchive[application.ArchiveName] = [..packages.Select( p => p.Reference )];
            }

            foreach ( var configuration in sourceConfigurations )
            {
                applicationsByConfiguration[configuration] =
                    applicationsByConfiguration[configuration].Select( a => a with { Packages = packagesByArchive[a.ArchiveName] } ).ToList();
            }

            applications = applicationsByConfiguration[sourceConfigurations[0]];

            generatedConfigurations = TestArchiveCells.Create( product, c => applicationsByConfiguration[c] );
            TestArchives.WriteList( context, applications );
        }

        // TeamCity
        if ( product.GenerateTeamCitySettings )
        {
            if ( !TeamCitySettingsFile.TryWrite( context, generatedConfigurations ) )
            {
                return false;
            }
        }

        EmbeddedResourceHelper.ExtractScript( context, "Build.ps1", "" );
        EmbeddedResourceHelper.ExtractScript( context, "build.sh", "" );

        // What a build leaves on an agent that the next build must not see. Generated for every product, Docker or not:
        // the stale packages it deletes reach the cache of the agent from any build, and a build configuration that
        // runs no container still needs them gone. It is also what keeps that logic out of the generated TeamCity
        // settings, where it used to be one very long inline command per build configuration.
        EmbeddedResourceHelper.ExtractScript( context, "CleanUpBuildAgent.ps1", product.EngineeringDirectory );

        // The launcher of the Docker-based tests. It is generated for a product that declares at least one
        // configuration running them, rather than for every product that uses Docker: the two are unrelated,
        // because the launcher executes on the agent and starts containers of its own instead of running inside
        // one.
        if ( product.AdditionalCiBuildConfigurations.Any( c => c is DockerTestsAdditionalCiBuildConfiguration ) )
        {
            EmbeddedResourceHelper.ExtractScript( context, "RunDockerTests.ps1", product.EngineeringDirectory );
        }

        // The runner of the test archives, for a product that publishes them; a product that does not has nothing for it
        // to run.
        if ( product.PublishesTestArchives )
        {
            EmbeddedResourceHelper.ExtractScript( context, TestArchives.ScriptName, product.EngineeringDirectory );
        }

        // The script that runs a command against every product of a consolidated build. Only a consolidated product has
        // one: it is the only product that drives the build of other repositories.
        if ( product.DependencyDefinition.IsConsolidated )
        {
            EmbeddedResourceHelper.ExtractScript( context, "Orchestrator.ps1", "" );
        }

        // Docker.
        if ( product.UseDocker )
        {
            EmbeddedResourceHelper.ExtractScript( context, "DockerBuild.ps1", "" );
            EmbeddedResourceHelper.ExtractScript( context, "RunClaude.ps1", "eng" );

            if ( product.GenerateDockerfiles )
            {
                var image = (ContainerRequirements) product.OverriddenBuildAgentRequirements!;

                // Generate the main image chain (build [+ the Visual Studio layer] + claude leaf).
                if ( !( image with { GenerateClaudeImage = true } ).WriteDockerfiles(
                        context,
                        additionalName: null,
                        extraComponents: [],
                        validateBuildComponents: true ) )
                {
                    return false;
                }

                // Generate a chain per additional Dockerfile.
                foreach ( var additionalDockerfile in product.AdditionalDockerfiles )
                {
                    var additionalImage = additionalDockerfile.Requirements ?? image;

                    if ( !additionalImage.WriteDockerfiles(
                            context,
                            additionalDockerfile.Name,
                            additionalDockerfile.Components,
                            validateBuildComponents: false ) )
                    {
                        return false;
                    }
                }
            }

            // Generate DockerMounts.g.ps1 to define additional mount points for dependencies.
            WriteDockerMounts( context );
        }

        context.Console.WriteSuccess( "Generating build scripts was successful." );

        return true;
    }

    /// <summary>
    /// Writes <c>eng/DockerMounts.g.ps1</c>, which maps the local directory of each dependency to a mount point of the
    /// build container. This file and the <c>Versions.*.g.props</c> file written with it are both excluded from source
    /// control and hold machine-local paths, so a problem here is reported as a warning and never fails the command.
    /// The tracked files written by <see cref="Execute"/> are its contract; this one is a local convenience, and
    /// commands such as the upstream merge regenerate the tracked files in a checkout where the dependencies have
    /// deliberately not been fetched.
    /// </summary>
    private static void WriteDockerMounts( BuildContext context )
    {
        const string skipped = "Skipping the generation of 'DockerMounts.g.ps1'";

        var buildSettings = new BuildSettings { BuildConfiguration = BuildConfiguration.Debug };
        buildSettings.Initialize( context );

        if ( !DependenciesConfigurationFile.TryLoad( context, buildSettings, buildSettings.BuildConfiguration, out var dependenciesOverrideFile ) )
        {
            context.Console.WriteWarning( $"{skipped}: the dependency configuration could not be read." );

            return;
        }

        // Writing DockerMounts.g.ps1 needs a resolved VersionFile for every non-feed dependency.
        // We do not fetch automatically here; the user is expected to have run 'dependencies fetch' first.
        var unfetchedDependencies = GetUnfetchedDependencies( dependenciesOverrideFile.Dependencies );

        if ( unfetchedDependencies.Length > 0 )
        {
            context.Console.WriteWarning(
                $"{skipped}: dependencies have not been fetched: {string.Join( ", ", unfetchedDependencies )}. Run './Build.ps1 dependencies fetch' first." );

            return;
        }

        if ( !dependenciesOverrideFile.TryWrite( context ) )
        {
            context.Console.WriteWarning( $"{skipped}: the file could not be written." );
        }
    }

    /// <summary>
    /// Returns the keys of the dependencies that have been neither fetched nor restored, i.e. whose version file is
    /// unknown. Feed dependencies are excluded because they are consumed from a package feed and have no local directory.
    /// </summary>
    internal static ImmutableArray<string> GetUnfetchedDependencies( IEnumerable<KeyValuePair<string, DependencySource>> dependencies )
        => dependencies
            .Where( d => d.Value.SourceKind != DependencySourceKind.Feed && d.Value.VersionFile == null )
            .Select( d => d.Key )
            .ToImmutableArray();
}