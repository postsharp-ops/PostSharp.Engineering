// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Build.Model;
using System.Runtime.InteropServices;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

/// <summary>
/// An implementation of <see cref="ManySolutions"/> that builds each scenario with the desktop (.NET Framework)
/// <c>MSBuild.exe</c> of a Visual Studio or Build Tools installation, instead of with <c>dotnet</c>.
/// </summary>
/// <remarks>
/// Use this type instead of <see cref="ManyDotNetSolutions"/> to cover behaviors that only manifest under the
/// .NET Framework-hosted compiler. The scenarios are skipped, with an explicit log line, on non-Windows platforms,
/// so that a cross-platform product definition stays valid.
/// </remarks>
[PublicAPI]
public class ManyMSBuildSolutions : ManySolutions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ManyMSBuildSolutions"/> class.
    /// </summary>
    /// <param name="directory">A directory.</param>
    public ManyMSBuildSolutions( string directory ) : base( directory ) { }

    /// <summary>
    /// Gets the full path of <c>MSBuild.exe</c>, for agents with a non-standard layout. When not set, MSBuild is
    /// located using <c>vswhere.exe</c>, or using the <c>ENG_MSBUILD_EXE</c> environment variable.
    /// </summary>
    public string? MSBuildExePath { get; init; }

    /// <summary>
    /// Gets the default MSBuild target of the scenarios. Defaults to <c>Build</c>. It can be overridden per scenario,
    /// and per matrix entry, by the <see cref="TestOptions.Target"/> property of <c>test.json</c>.
    /// </summary>
    public string DefaultTarget { get; init; } = "Build";

    protected override bool IsSupportedOnThisPlatform( BuildContext context )
    {
        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            return true;
        }

        context.Console.WriteImportantMessage(
            $"Skipping '{this.SolutionPath}': {nameof(ManyMSBuildSolutions)} requires the desktop MSBuild.exe, which is only available on Windows." );

        return false;
    }

    protected override TestableSolution CreateSolution( string projectPath, BuildMethod testMethod )
        => new MSBuildProjectSolution( projectPath )
        {
            EnvironmentVariables = this.EnvironmentVariables,
            TestMethod = testMethod,
            MSBuildExePath = this.MSBuildExePath,
            DefaultTarget = this.DefaultTarget
        };
}
