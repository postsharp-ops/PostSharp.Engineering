// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Build.Locator;
using Microsoft.VisualStudio.Setup.Configuration;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PostSharp.Engineering.BuildTools.Build.MSBuild;

// ReSharper disable once InconsistentNaming
internal static class MSBuildHelper
{
    private static bool _isInitialized;

    public static VisualStudioInstance? RegisteredInstance { get; private set; }

    public static void InitializeLocator()
    {
        if ( !_isInitialized )
        {
            if ( MSBuildLocator.CanRegister )
            {
                try
                {
                    RegisteredInstance = MSBuildLocator.RegisterDefaults();

                    _isInitialized = true;
                }
                catch ( Exception e )
                {
                    throw new InvalidOperationException(
                        $"Cannot find a suitable version of MSBuild for "
                        + $"{RuntimeInformation.FrameworkDescription} {RuntimeInformation.ProcessArchitecture}. "
                        + "You should probably install an SDK for this .NET version."
                        + "Try this: `winget install Microsoft.DotNet.Sdk.X`.",
                        e );
                }
            }
        }
    }

    /// <summary>
    /// The environment variable that overrides the discovery of the desktop <c>MSBuild.exe</c>.
    /// </summary>
    public const string MSBuildExeEnvironmentVariable = "ENG_MSBUILD_EXE";

    /// <summary>
    /// Finds the desktop (.NET Framework) <c>MSBuild.exe</c>. When a version is requested, either by
    /// <paramref name="msbuildVersion"/> or by <see cref="Product.MSBuildVersion"/>, only an installation matching that
    /// version is accepted. Otherwise, the latest installation is used.
    /// </summary>
    /// <param name="explicitPath">An explicit path that takes precedence over any discovery, or <c>null</c>. When
    /// <c>null</c>, the <c>ENG_MSBUILD_EXE</c> environment variable is used instead.</param>
    /// <returns>The path of <c>MSBuild.exe</c>, or <c>null</c> if none was found. In the latter case, an actionable
    /// error has been written to the console.</returns>
    public static string? FindMSBuildExe( BuildContext context, Version? msbuildVersion = null, string? explicitPath = null )
    {
        var overridePath = explicitPath ?? Environment.GetEnvironmentVariable( MSBuildExeEnvironmentVariable );

        if ( !string.IsNullOrEmpty( overridePath ) )
        {
            if ( !File.Exists( overridePath ) )
            {
                context.Console.WriteError(
                    $"The MSBuild executable '{overridePath}' does not exist. It was set by "
                    + (explicitPath == null ? $"the {MSBuildExeEnvironmentVariable} environment variable." : "an explicit property.") );

                return null;
            }

            return overridePath;
        }

        var requestedVersion = msbuildVersion ?? context.Product.MSBuildVersion;

        var instances = GetMSBuildInstances( context, true )
            .Where( i => Directory.Exists( Path.Combine( i.Path, "MSBuild", "Current", "Bin" ) ) )
            .OrderByDescending( i => i.Version )
            .ThenByDescending( i => i.FullVersion )
            .ToList();

        if ( requestedVersion == null )
        {
            // No version is pinned, so any installation will do. vswhere is more reliable than the setup API, which
            // is why it is tried first, but the setup API remains as a fallback.
            return FindLatestMSBuildExe( context, instances );
        }

        var instance = instances
            .FirstOrDefault( i => MatchVersionComponent( requestedVersion.Major, i.Version.Major )
                                  && MatchVersionComponent( requestedVersion.Minor, i.Version.Minor )
                                  && MatchVersionComponent( requestedVersion.Build, i.Version.Build )
                                  && MatchVersionComponent( requestedVersion.Revision, i.Version.Revision ) );

        static bool MatchVersionComponent( int requested, int supplied ) => requested < 0 || requested == supplied;

        if ( instance == null )
        {
            var availableDescription = instances.Count == 0
                ? "No Visual Studio installation with MSBuild was found on this machine."
                : "The following Visual Studio installations were found: "
                  + string.Join( ", ", instances.Select( i => $"{i.Name} (version {i.Version})" ) ) + ".";

            context.Console.WriteError(
                $"Could not find msbuild.exe matching the required MSBuild version '{requestedVersion}'. {availableDescription} "
                + "Install the matching Visual Studio version (including the MSBuild component), "
                + $"or change the Product.{nameof(Product.MSBuildVersion)} property to match an installed version." );

            return null;
        }

        return Path.Combine( instance.Path, "MSBuild", "Current", "Bin", "msbuild.exe" );
    }

    /// <summary>
    /// The fallback of <see cref="FindMSBuildExe"/> used when no MSBuild version is pinned: returns the
    /// <c>MSBuild.exe</c> of the latest Visual Studio or Build Tools installation.
    /// </summary>
    private static string? FindLatestMSBuildExe( BuildContext context, List<MSBuildInstance> instances )
    {
        var fromVsWhere = FindMSBuildExeUsingVsWhere( context );

        if ( fromVsWhere != null )
        {
            return fromVsWhere;
        }

        var instance = instances.FirstOrDefault( i => File.Exists( Path.Combine( i.Path, "MSBuild", "Current", "Bin", "MSBuild.exe" ) ) );

        if ( instance != null )
        {
            return Path.Combine( instance.Path, "MSBuild", "Current", "Bin", "MSBuild.exe" );
        }

        context.Console.WriteError(
            "Could not find the desktop MSBuild.exe. Install Visual Studio or the Visual Studio Build Tools with the "
            + "'Microsoft.Component.MSBuild' component (the 'MSBuild' component of the '.NET desktop build tools' workload), "
            + $"or set the {MSBuildExeEnvironmentVariable} environment variable to the full path of MSBuild.exe." );

        return null;
    }

    private static string? FindMSBuildExeUsingVsWhere( BuildContext context )
    {
        var programFiles = Environment.GetEnvironmentVariable( "ProgramFiles(x86)" ) ?? Environment.GetEnvironmentVariable( "ProgramFiles" );

        if ( programFiles == null )
        {
            return null;
        }

        var vsWherePath = Path.Combine( programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe" );

        if ( !File.Exists( vsWherePath ) )
        {
            return null;
        }

        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                vsWherePath,
                @"-products * -requires Microsoft.Component.MSBuild -latest -find MSBuild\**\Bin\MSBuild.exe",
                Environment.CurrentDirectory,
                out var exitCode,
                out var output,
                new ToolInvocationOptions { FilterOutput = false } )
             || exitCode != 0 )
        {
            context.Console.WriteWarning( $"'{vsWherePath}' failed with exit code {exitCode}." );

            return null;
        }

        var found = output
            .Split( '\r', '\n' )
            .Select( l => l.Trim() )
            .FirstOrDefault( l => l.EndsWith( "MSBuild.exe", StringComparison.OrdinalIgnoreCase ) && File.Exists( l ) );

        if ( found == null )
        {
            return null;
        }

        // The `-find` pattern matches `Bin\MSBuild.exe`, i.e. the 32-bit host. Prefer its 64-bit sibling in
        // `Bin\amd64`, because the 32-bit MSBuild runs out of memory on large builds.
        var amd64 = Path.Combine( Path.GetDirectoryName( found )!, "amd64", "MSBuild.exe" );

        return File.Exists( amd64 ) ? amd64 : found;
    }

    public static IEnumerable<MSBuildInstance> GetMSBuildInstances( BuildContext context, bool vsOnly = false )
    {
        List<MSBuildInstance> list = new();

        // List from MSBuildLocator.
        if ( !vsOnly )
        {
            // MSBuildLocator will not return VS installations when executed from .NET Core.
            foreach ( var instance in MSBuildLocator.QueryVisualStudioInstances(
                         new VisualStudioInstanceQueryOptions()
                         {
                             AllowAllRuntimeVersions = true,
                             DiscoveryTypes = DiscoveryType.DotNetSdk | DiscoveryType.DeveloperConsole | DiscoveryType.VisualStudioSetup
                         } ) )
            {
                var fullVersion = instance.DiscoveryType == DiscoveryType.DotNetSdk ? Path.GetFileName( instance.MSBuildPath ) : instance.Version.ToString();

                list.Add(
                    new MSBuildInstance(
                        $"{instance.Name} {instance.Version}",
                        instance.Version,
                        fullVersion,
                        instance.MSBuildPath,
                        $"MSBuildLocator:{instance.DiscoveryType}" ) );
            }
        }

        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            // List from Visual Studio installer.
            try
            {
                // List instances discovered by Visual Studio installer.
                var query = new SetupConfiguration();
                var enumInstances = query.EnumAllInstances();

                int fetched;
                var instances = new ISetupInstance[1];

                do
                {
                    enumInstances.Next( 1, instances, out fetched );

                    if ( fetched > 0 )
                    {
                        var instance = (ISetupInstance2) instances[0];

                        list.Add(
                            new MSBuildInstance(
                                instance.GetDisplayName(),
                                Version.Parse( instance.GetInstallationVersion() ),
                                instance.GetInstallationVersion(),
                                instance.GetInstallationPath(),
                                "VisualStudio" ) );
                    }
                }
                while ( fetched > 0 );
            }
            catch ( COMException exception )
            {
                context.Console.WriteWarning( $"Cannot find VS instances: {exception.Message}" );

                return [];
            }
        }

        return list;
    }
}