// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Build.Locator;
using Microsoft.VisualStudio.Setup.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.BuildTools;

internal record VisualStudioInstance( string Name, Version Version, string Path );

// ReSharper disable once InconsistentNaming
internal static class MSBuildHelper
{
    private static bool _isInitialized;

    public static void InitializeLocator()
    {
        if ( !_isInitialized )
        {
            if ( MSBuildLocator.CanRegister )
            {
                try
                {
                    MSBuildLocator.RegisterDefaults();

                    _isInitialized = true;
                }
                catch ( Exception e )
                {
                    throw new InvalidOperationException(
                        $"Cannot find a suitable version of MSBuild for "
                        + $"{RuntimeInformation.FrameworkDescription} {RuntimeInformation.ProcessArchitecture}. "
                        + "You should probably install an SDK for this .NET version."
                        + "Try this: `winget install Microsoft.DotNet.Sdk.6`.",
                        e );
                }
            }
        }
    }

    public static string? FindLatestMSBuildExe( BuildContext context )
    {
        var directory = FindLatestMSBuildDirectory( context );

        if ( directory == null )
        {
            return directory;
        }
        else
        {
            return Path.Combine( directory, "MSBuild.exe" );
        }
    }

    private static string? FindLatestMSBuildDirectory( BuildContext context )
    {
        var instances = GetVisualStudioInstances( context ).OrderByDescending( i => i.Version );

        foreach ( var instance in instances )
        {
            // We got a Visual Studio instance but not all of them have an MSBuild instance. For instance, a Test Agent instance does not have.
            var directory = Path.Combine( instance.Path, "MSBuild", "Current", "Bin" );

            if ( Directory.Exists( directory ) )
            {
                return directory;
            }
        }

        return null;
    }

    public static IEnumerable<VisualStudioInstance> GetVisualStudioInstances( BuildContext context )
    {
        if ( !RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            return [];
        }

        List<VisualStudioInstance> list = new();

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
                        new VisualStudioInstance(
                            instance.GetDisplayName(),
                            Version.Parse( instance.GetInstallationVersion() ),
                            instance.GetInstallationPath() ) );
                }
            }
            while ( fetched > 0 );
        }
        catch ( COMException exception )
        {
            context.Console.WriteWarning( $"Cannot find VS instances: {exception.Message}" );

            return [];
        }

        return list;
    }
}