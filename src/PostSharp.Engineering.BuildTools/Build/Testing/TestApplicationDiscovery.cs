// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace PostSharp.Engineering.BuildTools.Build.Testing;

/// <summary>
/// Finds the Microsoft.Testing.Platform test applications of a product by evaluating the projects of its solutions, without
/// building or restoring them.
/// </summary>
/// <remarks>
/// <para>
/// Only the solutions whose <see cref="TestRunner"/> is <see cref="TestRunner.MicrosoftTestingPlatform"/> are read: a
/// repository can contain hundreds of other projects, and evaluating them would cost time for nothing.
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

    [MethodImpl( MethodImplOptions.NoInlining )]
    private static bool TryDiscoverCore( BuildContext context, BuildConfiguration configuration, out ImmutableArray<TestApplication> applications )
    {
        var stopwatch = Stopwatch.StartNew();
        var builder = ImmutableArray.CreateBuilder<TestApplication>();
        var product = context.Product;

        var globalProperties = new Dictionary<string, string>
        {
            ["Configuration"] = product.DependencyDefinition.MSBuildConfiguration[configuration]
        };

        using var collection = new ProjectCollection( globalProperties );
        var projectCount = 0;

        foreach ( var solution in product.Solutions.Where( IsTestingPlatformSolution ) )
        {
            var solutionPath = Path.Combine( context.RepoDirectory, solution.SolutionPath );

            if ( !TestingPlatformTestRunner.TryGetProjects( context, solutionPath, out var projects ) )
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

        context.Console.WriteMessage(
            $"Found {builder.Count} test application(s) in {projectCount} project(s) in {stopwatch.Elapsed.TotalSeconds:F1} s." );

        applications = builder.ToImmutable();

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

        return duplicates.Count == 0;
    }

    private static bool IsTestingPlatformSolution( Solution solution )
        => solution switch
        {
            DotNetSolution dotNetSolution => dotNetSolution.TestRunner == TestRunner.MicrosoftTestingPlatform,
            MsbuildSolution msbuildSolution => msbuildSolution.TestRunner == TestRunner.MicrosoftTestingPlatform,
            _ => false
        };

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
                skip.Length == 0 ? null : skip ) );
    }

    private static bool IsTestApplication( Project project )
    {
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
