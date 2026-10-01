// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PostSharp.Engineering.BuildTools.Build.Testing;

/// <summary>
/// Finds the Microsoft.Testing.Platform test applications of a product by evaluating the projects of its solutions, without
/// building or restoring them.
/// </summary>
/// <remarks>
/// <para>
/// Only the solutions whose <see cref="Solution.ContainsTestApplications"/> is set are read: a repository can contain
/// hundreds of other projects, and evaluating them would cost time for nothing.
/// </para>
/// <para>
/// A project is a test application when it says so in a property that it sets itself. <c>IsTestingPlatformApplication</c>
/// is set by the packages of the test frameworks, which are imported only after a restore, so a checkout that has never
/// been restored does not see it. <c>UseMicrosoftTestingPlatformRunner</c> (xunit.v3) and <c>EnableMSTestRunner</c> (MSTest)
/// are set by the project, and are therefore read too. An explicit <c>IsTestingPlatformApplication=false</c> wins, which is
/// how a project that references a test project, and receives the props of its test framework, says that it is not one.
/// </para>
/// </remarks>
internal static class TestApplicationDiscovery
{
    public static bool TryDiscover( BuildContext context, BuildConfiguration configuration, out ImmutableArray<TestApplication> applications )
    {
        // The MSBuild assemblies are loaded by the locator, so no method that uses one of their types may be compiled before
        // this call.
        MSBuildHelper.InitializeLocator();

        return TryDiscoverCore( context, configuration, out applications );
    }

    /// <summary>
    /// Finds the test applications of one solution, solution filter or project, whether or not its solution sets
    /// <see cref="Solution.ContainsTestApplications"/>, and without the checks that only the test archives need.
    /// </summary>
    public static bool TryDiscover( BuildContext context, BuildConfiguration configuration, string solutionPath, out ImmutableArray<TestApplication> applications )
    {
        MSBuildHelper.InitializeLocator();

        return TryEvaluate( context, configuration, [solutionPath], out applications, out _ );
    }

    private static bool TryDiscoverCore( BuildContext context, BuildConfiguration configuration, out ImmutableArray<TestApplication> applications )
    {
        var stopwatch = Stopwatch.StartNew();
        var product = context.Product;

        var solutionPaths = product.Solutions.Where( s => s.ContainsTestApplications ).Select( s => Path.Combine( context.RepoDirectory, s.SolutionPath ) );

        if ( !TryEvaluate( context, configuration, solutionPaths, out applications, out var projectCount ) )
        {
            return false;
        }

        context.Console.WriteMessage(
            $"Found {applications.Length} test application(s) in {projectCount} project(s) in {stopwatch.Elapsed.TotalSeconds:F1} s." );

        // Two applications with one archive name would write one archive, and one of them would never be tested. The usual
        // cause is a project built for two processor architectures under one assembly name: each build sets
        // RuntimeIdentifier, which is part of the name.
        var duplicates = applications.GroupBy( a => a.ArchiveName, StringComparer.OrdinalIgnoreCase ).Where( g => g.Count() > 1 ).ToList();

        foreach ( var duplicate in duplicates )
        {
            context.Console.WriteError(
                $"The test applications of {string.Join( " and ", duplicate.Select( a => $"'{Path.GetRelativePath( context.RepoDirectory, a.ProjectPath )}'" ) )} "
                + $"have the same archive name, '{duplicate.Key}'. Give them different assembly names, or set RuntimeIdentifier." );
        }

        // An artifact is downloaded to its own path, which must therefore name files of a directory of the repository.
        var invalidArtifacts = applications
            .SelectMany( a => a.Artifacts.Where( x => !IsValidArtifact( x ) ).Select( x => (Application: a, Artifact: x) ) )
            .ToList();

        foreach ( var invalid in invalidArtifacts )
        {
            context.Console.WriteError(
                $"The artifact '{invalid.Artifact}' of '{Path.GetRelativePath( context.RepoDirectory, invalid.Application.ProjectPath )}' is not valid. "
                + "Give a path relative to the repository, in which only the file name can contain wildcards." );
        }

        return duplicates.Count == 0 && invalidArtifacts.Count == 0;
    }

    // The MSBuild assemblies are loaded by the locator, so this method, which uses their types, must not be inlined into a
    // caller that runs before MSBuildHelper.InitializeLocator.
    [MethodImpl( MethodImplOptions.NoInlining )]
    private static bool TryEvaluate(
        BuildContext context,
        BuildConfiguration configuration,
        IEnumerable<string> solutionPaths,
        out ImmutableArray<TestApplication> applications,
        out int projectCount )
    {
        var builder = ImmutableArray.CreateBuilder<TestApplication>();

        var globalProperties = new Dictionary<string, string>
        {
            ["Configuration"] = context.Product.DependencyDefinition.MSBuildConfiguration[configuration]
        };

        using var collection = new ProjectCollection( globalProperties );
        projectCount = 0;

        foreach ( var solutionPath in solutionPaths )
        {
            if ( !TryGetProjects( context, solutionPath, out var projects ) )
            {
                applications = default;

                return false;
            }

            foreach ( var projectPath in projects )
            {
                projectCount++;

                try
                {
                    AddApplications( collection, projectPath, builder );
                }
                catch ( InvalidProjectFileException e )
                {
                    context.Console.WriteError( $"Cannot evaluate '{projectPath}': {e.Message}" );

                    applications = default;

                    return false;
                }
                finally
                {
                    collection.UnloadAllProjects();
                }
            }
        }

        applications = builder.ToImmutable();

        return true;
    }

    private static bool IsValidArtifact( string artifact )
    {
        var directory = TestApplication.GetArtifactDirectory( artifact );

        return !Path.IsPathRooted( artifact )
               && directory.Length > 0
               && artifact.IndexOfAny( ['=', '>', ':'] ) < 0
               && directory.IndexOfAny( ['*', '?'] ) < 0
               && directory.Split( '/' ).All( s => s is not ("" or "." or "..") );
    }

    private static readonly string[] _projectExtensions = [".csproj", ".vbproj", ".fsproj"];

    /// <summary>
    /// Gets the managed projects of a solution, solution filter or project, by their full path.
    /// </summary>
    internal static bool TryGetProjects( BuildContext context, string solutionPath, out IReadOnlyList<string> projects )
    {
        var extension = Path.GetExtension( solutionPath );

        if ( extension.Equals( ".slnf", StringComparison.OrdinalIgnoreCase ) )
        {
            // A solution filter names the projects by a path relative to the directory of its solution.
            using var document = JsonDocument.Parse( File.ReadAllText( solutionPath ) );
            var solutionElement = document.RootElement.GetProperty( "solution" );
            var filteredSolution = Path.GetFullPath( solutionElement.GetProperty( "path" ).GetString()!, Path.GetDirectoryName( solutionPath )! );

            projects = solutionElement.GetProperty( "projects" )
                .EnumerateArray()
                .Select( p => Path.GetFullPath( p.GetString()!.Replace( '\\', Path.DirectorySeparatorChar ), Path.GetDirectoryName( filteredSolution )! ) )
                .Where( IsManagedProject )
                .ToList();

            return true;
        }

        if ( !extension.Equals( ".sln", StringComparison.OrdinalIgnoreCase ) && !extension.Equals( ".slnx", StringComparison.OrdinalIgnoreCase ) )
        {
            projects = [solutionPath];

            return true;
        }

        if ( !ToolInvocationHelper.InvokeTool( context.Console, "dotnet", $"sln \"{solutionPath}\" list", context.RepoDirectory, out _, out var output ) )
        {
            context.Console.WriteError( $"Cannot list the projects of '{solutionPath}'." );
            context.Console.WriteError( output );
            projects = [];

            return false;
        }

        // The output has a header, and names each project by a path relative to the directory of the solution.
        projects = output
            .Split( '\r', '\n' )
            .Select( l => l.Trim() )
            .Where( IsManagedProject )
            .Select( p => Path.GetFullPath( p.Replace( '\\', Path.DirectorySeparatorChar ), Path.GetDirectoryName( solutionPath )! ) )
            .ToList();

        return true;
    }

    // A test application is a managed project. The other projects of a solution, such as a shared project or a native one,
    // cannot always be evaluated alone.
    private static bool IsManagedProject( string path ) => _projectExtensions.Any( e => path.EndsWith( e, StringComparison.OrdinalIgnoreCase ) );

    private static void AddApplications( ProjectCollection collection, string projectPath, ImmutableArray<TestApplication>.Builder applications )
    {
        var project = collection.LoadProject( projectPath );
        var targetFrameworks = project.GetPropertyValue( "TargetFrameworks" );

        if ( string.IsNullOrWhiteSpace( targetFrameworks ) )
        {
            AddApplication( project, projectPath, applications );

            return;
        }

        foreach ( var targetFramework in targetFrameworks.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).Distinct() )
        {
            var innerProject = collection.LoadProject(
                projectPath,
                new Dictionary<string, string>( collection.GlobalProperties ) { ["TargetFramework"] = targetFramework },
                null );

            AddApplication( innerProject, projectPath, applications );
        }
    }

    private static void AddApplication( Project project, string projectPath, ImmutableArray<TestApplication>.Builder applications )
    {
        if ( !IsTestApplication( project ) )
        {
            return;
        }

        var skip = project.GetPropertyValue( "TestApplicationSkip" );

        applications.Add(
            new TestApplication(
                projectPath,
                project.GetPropertyValue( "AssemblyName" ),
                project.GetPropertyValue( "TargetFramework" ),
                project.GetPropertyValue( "TestApplicationRuntimeIdentifier" ),
                Split( project.GetPropertyValue( "TestApplicationPlatforms" ) ),
                project.GetItems( "TestApplicationTag" ).Select( i => i.EvaluatedInclude ).Distinct().ToImmutableArray(),
                IsTrue( project, "TestApplicationRunAlone" ),
                skip.Length == 0 ? null : skip,
                Split( project.GetPropertyValue( "TestApplicationArtifacts" ) ).Select( x => x.Replace( '\\', '/' ) ).Distinct().ToImmutableArray() )
            {
                ProjectAssetsFile = IsScript( project ) ? "" : project.GetPropertyValue( "ProjectAssetsFile" ),
                TargetFrameworkMoniker = project.GetPropertyValue( "TargetFrameworkMoniker" )
            } );
    }

    private static bool IsScript( Project project ) => string.Equals( project.GetPropertyValue( "TestApplicationKind" ), "ps1", StringComparison.OrdinalIgnoreCase );

    private static bool IsTestApplication( Project project )
    {
        // A project that describes an archive whose entry is a PowerShell script, which runs the tests itself.
        if ( IsScript( project ) )
        {
            return true;
        }

        var isTestingPlatformApplication = project.GetPropertyValue( "IsTestingPlatformApplication" );

        if ( isTestingPlatformApplication.Length > 0 )
        {
            return bool.TryParse( isTestingPlatformApplication, out var value ) && value;
        }

        return IsTrue( project, "UseMicrosoftTestingPlatformRunner" ) || IsTrue( project, "EnableMSTestRunner" );
    }

    private static bool IsTrue( Project project, string property ) => bool.TryParse( project.GetPropertyValue( property ), out var value ) && value;

    private static ImmutableArray<string> Split( string value )
        => value.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).ToImmutableArray();
}
