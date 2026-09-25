// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Runs the <c>Build.ps1</c> that <c>generate-scripts</c> extracts against a minimal build project.
/// </summary>
public class BuildScriptTests
{
    private const string _programOutput = "The current build program ran.";

    // The bin/Debug directory of the build project keeps the output of every target framework that the project has
    // had. The script used to run the first match of a directory search, and 'net1.0' sorts before the current target
    // framework, so it ran the output of the former target framework instead of the program it had just built.
    [Fact]
    public void Execute_RunsTheOutputOfTheCurrentTargetFramework()
    {
        var powerShell = DockerBuildScript.FindPowerShell();

        if ( powerShell == null )
        {
            return;
        }

        using var directory = new TempDirectory();

        var engSrcDirectory = Path.Combine( directory.Path, "eng", "src" );
        Directory.CreateDirectory( engSrcDirectory );

        File.WriteAllText(
            Path.Combine( engSrcDirectory, "BuildProbe.csproj" ),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <OutputType>Exe</OutputType>
                 <TargetFramework>net{Environment.Version.Major}.0</TargetFramework>
               </PropertyGroup>
             </Project>
             """ );

        File.WriteAllText( Path.Combine( engSrcDirectory, "Program.cs" ), $"System.Console.WriteLine( \"{_programOutput}\" );" );

        File.WriteAllText(
            Path.Combine( directory.Path, "Build.ps1" ),
            ReadScript().Replace( "<ENG_PATH>", "eng", StringComparison.Ordinal ).Replace( "<PRODUCT_NAME>", "Probe", StringComparison.Ordinal ),
            new UTF8Encoding( false ) );

        // The output of a former target framework. It is not an assembly, so running it fails. It is newer than every
        // source, which is the state that the former script produced itself, because it set the time of the file it ran.
        var staleOutputDirectory = Path.Combine( engSrcDirectory, "bin", "Debug", "net1.0" );
        Directory.CreateDirectory( staleOutputDirectory );
        var staleOutput = Path.Combine( staleOutputDirectory, "BuildProbe.dll" );
        File.WriteAllText( staleOutput, "This is not an assembly." );
        File.SetLastWriteTime( staleOutput, DateTime.Now.AddMinutes( 1 ) );

        // The first run has no recorded output, so it builds the project.
        var firstOutput = Run( powerShell, directory.Path );
        Assert.Contains( "Building BuildProbe", firstOutput, StringComparison.Ordinal );
        Assert.Contains( _programOutput, firstOutput, StringComparison.Ordinal );

        // The second run takes the output that the first run recorded, without building.
        var secondOutput = Run( powerShell, directory.Path );
        Assert.DoesNotContain( "Building BuildProbe", secondOutput, StringComparison.Ordinal );
        Assert.Contains( _programOutput, secondOutput, StringComparison.Ordinal );
    }

    private static string ReadScript()
    {
        const string resourceName = "PostSharp.Engineering.BuildTools.Resources.Build.ps1";

        using var stream = typeof(Product).Assembly.GetManifestResourceStream( resourceName )
                           ?? throw new InvalidOperationException( $"Cannot find the embedded resource '{resourceName}'." );

        using var reader = new StreamReader( stream );

        return reader.ReadToEnd();
    }

    private static string Run( string powerShell, string repositoryDirectory )
    {
        var startInfo = new ProcessStartInfo( powerShell, "-NoProfile -NonInteractive -File Build.ps1" )
        {
            WorkingDirectory = repositoryDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };

        // Other tests of this process call MSBuildHelper.InitializeLocator, which points these variables at the SDK that
        // the locator selected. Inherited, they make 'dotnet build' in the script load the tasks of that SDK into the
        // MSBuild of another one, which fails with a MissingMethodException.
        startInfo.Environment.Remove( "MSBUILD_EXE_PATH" );
        startInfo.Environment.Remove( "MSBuildExtensionsPath" );
        startInfo.Environment.Remove( "MSBuildSDKsPath" );

        using var process = Process.Start( startInfo )!;

        var standardError = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd() + standardError.Result;
        process.WaitForExit( 300_000 );

        Assert.True( process.ExitCode == 0, $"Build.ps1 exited with {process.ExitCode}:\n{output}" );

        return output;
    }
}
