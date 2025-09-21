// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Build.Locator;
using Microsoft.VisualStudio.Setup.Configuration;
using PostSharp.Engineering.BuildTools.Build.Model;
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

    public static string? FindMSBuildExe( BuildContext context, Version? msbuildVersion = null )
    {
        var instances = GetMSBuildInstances( context, true )
            .Where( i => Directory.Exists( Path.Combine( i.Path, "MSBuild", "Current", "Bin" ) ) )
            .OrderByDescending( i => i.Version )
            .ThenByDescending( i => i.FullVersion )
            .ToList();

        MSBuildInstance? instance;
        var requestedVersion = msbuildVersion ?? context.Product.MSBuildVersion;

        if ( requestedVersion == null )
        {
            throw new InvalidOperationException( $"Cannot use MSBuild because the Product.{nameof(Product.MSBuildVersion)} property is not defined." );
        }
        else
        {
            instance = instances
                .FirstOrDefault( i => MatchVersionComponent( requestedVersion.Major, i.Version.Major )
                                      && MatchVersionComponent( requestedVersion.Minor, i.Version.Minor )
                                      && MatchVersionComponent( requestedVersion.Build, i.Version.Build )
                                      && MatchVersionComponent( requestedVersion.Revision, i.Version.Revision ) );

            static bool MatchVersionComponent( int requested, int supplied ) => requested < 0 || requested == supplied;
        }

        if ( instance == null )
        {
            return null;
        }
        else
        {
            return Path.Combine( instance.Path, "MSBuild", "Current", "Bin", "msbuild.exe" );
        }
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