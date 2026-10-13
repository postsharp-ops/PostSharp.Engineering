// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Executes <c>CleanXmlDoc.targets</c> with <c>dotnet msbuild</c>. The target is not covered by the build of this
/// repository, because no project here produces a documentation file, so a defect in it is only visible when the target
/// is run on purpose.
/// </summary>
public sealed class CleanXmlDocTargetsTests : IDisposable
{
    private readonly string _directory = Path.Combine( Path.GetTempPath(), $"clean-xml-doc-{Guid.NewGuid():N}" );

    private static string SdkDirectory
    {
        get
        {
            var directory = AppContext.BaseDirectory;

            while ( directory != null && !File.Exists( Path.Combine( directory, "src", "PostSharp.Engineering.Sdk", "CleanXmlDoc.targets" ) ) )
            {
                directory = Path.GetDirectoryName( directory );
            }

            Assert.NotNull( directory );

            return Path.Combine( directory, "src", "PostSharp.Engineering.Sdk" );
        }
    }

    /// <summary>
    /// Writes a project that imports the target and has a stub <c>Build</c> target, so that the test does not depend on
    /// the .NET SDK, on a restore, or on the compiler. When <paramref name="deleteProjectFile"/> is <c>true</c>, the project
    /// deletes its own file before <c>Build</c> runs, which gives the target a <c>MSBuildProjectFullPath</c> that does not
    /// exist. A real build cannot start from a project file that is already missing, so this is the only way to reproduce
    /// that state.
    /// </summary>
    private string CreateProject( string? engineeringExePath, bool createDocumentationFile = false, bool deleteProjectFile = false )
    {
        Directory.CreateDirectory( this._directory );

        if ( createDocumentationFile )
        {
            Directory.CreateDirectory( Path.Combine( this._directory, "bin" ) );
            File.WriteAllText( Path.Combine( this._directory, "bin", "Test.xml" ), "<doc><members /></doc>" );
        }

        var deleteProjectFileTarget = deleteProjectFile
            ? """
                  <Target Name="DeleteProjectFile" BeforeTargets="Build">
                      <Delete Files="$(MSBuildProjectFullPath)" />
                  </Target>
              """
            : "";

        var projectPath = Path.Combine( this._directory, "Test.proj" );

        File.WriteAllText(
            projectPath,
            $"""
             <Project>
                 <PropertyGroup>
                     <GenerateDocumentationFile>True</GenerateDocumentationFile>
                     <TargetFramework>net8.0</TargetFramework>
                     <DocumentationFile>obj\Test.xml</DocumentationFile>
                     <OutDir>bin\</OutDir>
                     <PostSharpEngineeringExePath>{engineeringExePath}</PostSharpEngineeringExePath>
                 </PropertyGroup>
                 <Import Project="{Path.Combine( SdkDirectory, "CleanXmlDoc.targets" )}" />
                 <Target Name="Build" />
             {deleteProjectFileTarget}
             </Project>
             """ );

        return projectPath;
    }

    private static (int ExitCode, string Output) Build( string projectPath )
    {
        ToolInvocationHelper.InvokeTool(
            new ConsoleHelper(),
            "dotnet",
            $"msbuild \"{projectPath}\" -t:Build -nologo",
            Path.GetDirectoryName( projectPath ),
            out var exitCode,
            out var output,
            new ToolInvocationOptions { FilterOutput = false } );

        return (exitCode, output);
    }

    [Fact]
    public void UndefinedExePathFailsWithTheIntendedMessage()
    {
        var (exitCode, output) = Build( this.CreateProject( null ) );

        Assert.NotEqual( 0, exitCode );

        // MSB4064 is reported when a task is given a parameter it does not declare. It would hide the real cause.
        Assert.DoesNotContain( "MSB4064", output, StringComparison.Ordinal );
        Assert.Contains( "The PostSharpEngineeringExePath property is not defined", output, StringComparison.Ordinal );
    }

    [Fact]
    public void MissingDocumentationFileWarnsAndSucceeds()
    {
        var (exitCode, output) = Build( this.CreateProject( "dummy.dll" ) );

        Assert.Equal( 0, exitCode );
        Assert.DoesNotContain( "MSB4064", output, StringComparison.Ordinal );
        Assert.Contains( "does not exist, so its internal members cannot be removed", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// The control case of <see cref="MissingProjectFileSkipsTheTarget"/>. Without it, that test would also pass if the
    /// target were skipped for another reason, such as a documentation file that was not written.
    /// </summary>
    [Fact]
    public void ExistingProjectFileRunsTheTarget()
    {
        var (exitCode, output) = Build( this.CreateProject( "dummy.dll", createDocumentationFile: true ) );

        Assert.NotEqual( 0, exitCode );
        Assert.Contains( "xmldoc", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// A file-based app, which is built by <c>dotnet run Program.cs</c>, is compiled from a virtual project that is never
    /// written to disk, while its output directory is an absolute path under the temporary directory. The documentation file
    /// therefore exists although the project file does not, and the command cannot open a project that does not exist.
    /// </summary>
    [Fact]
    public void MissingProjectFileSkipsTheTarget()
    {
        var (exitCode, output) = Build( this.CreateProject( "dummy.dll", createDocumentationFile: true, deleteProjectFile: true ) );

        Assert.Equal( 0, exitCode );
        Assert.DoesNotContain( "xmldoc", output, StringComparison.Ordinal );
    }

    /// <summary>
    /// The <c>Error</c> and <c>Warning</c> tasks take the message in a <c>Text</c> parameter. A <c>Message</c> parameter
    /// is accepted by the project loader but fails the build when the task runs.
    /// </summary>
    [Fact]
    public void EveryErrorAndWarningTaskUsesTheTextParameter()
    {
        var files = Directory
            .EnumerateFiles( SdkDirectory, "*.*" )
            .Where( f => f.EndsWith( ".props", StringComparison.OrdinalIgnoreCase ) || f.EndsWith( ".targets", StringComparison.OrdinalIgnoreCase ) )
            .ToList();

        Assert.NotEmpty( files );

        foreach ( var file in files )
        {
            var tasks = XDocument
                .Load( file )
                .Descendants()
                .Where( e => e.Name.LocalName is "Error" or "Warning" );

            foreach ( var task in tasks )
            {
                Assert.True(
                    task.Attribute( "Text" ) != null,
                    $"The '{task.Name.LocalName}' task in '{Path.GetFileName( file )}' does not have a 'Text' attribute." );
            }
        }
    }

    public void Dispose()
    {
        if ( Directory.Exists( this._directory ) )
        {
            Directory.Delete( this._directory, true );
        }
    }
}