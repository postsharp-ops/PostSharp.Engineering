// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Model;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Testing;

/// <summary>
/// Executes the tests.
/// </summary>
[UsedImplicitly]
internal class TestCommand : BaseCommand<BuildSettings>
{
    protected override bool ExecuteCore( BuildContext context, BuildSettings settings ) => Execute( context, settings );

    public static bool Execute( BuildContext context, BuildSettings settings )
    {
        if ( !settings.NoDependencies && !BuildCommand.Execute( context, settings ) )
        {
            return false;
        }

        var product = context.Product;

        ImmutableDictionary<string, string> properties;
        var testResultsDir = Path.Combine( context.RepoDirectory, "TestResults" );

        if ( settings.AnalyzeCoverage )
        {
            // Removing the TestResults directory so that we reset the code coverage information.
            if ( Directory.Exists( testResultsDir ) )
            {
                Directory.Delete( testResultsDir, true );
            }

            properties = settings.AnalyzeCoverage
                ? ImmutableDictionary.Create<string, string>()
                    .Add( "CollectCoverage", "True" )
                    .Add( "CoverletOutput", testResultsDir + Path.DirectorySeparatorChar )
                : ImmutableDictionary<string, string>.Empty;
        }
        else
        {
            properties = ImmutableDictionary<string, string>.Empty;
        }

        Solution[] solutionsToTest;

        if ( settings.SolutionId != null )
        {
            var solution = product.Solutions[settings.SolutionId.Value - 1];
            solutionsToTest = [solution];
        }
        else
        {
            solutionsToTest = product.Solutions;
        }

        if ( settings.TestsFilter == null && product.DefaultTestsFilter != null )
        {
            settings = settings.WithTestsFilter( product.DefaultTestsFilter );
        }

        foreach ( var solution in solutionsToTest )
        {
            var solutionSettings = settings;

            if ( settings.AnalyzeCoverage && solution.SupportsTestCoverage )
            {
                solutionSettings = settings.WithAdditionalProperties( properties.ToImmutableDictionary() ).WithoutConcurrency();
            }

            context.Console.WriteHeading( $"Testing {solution.Name}" );

            if ( !solution.Execute( context, solutionSettings, solution.TestMethod ?? BuildMethod.Test ) )
            {
                return false;
            }

            context.Console.WriteSuccess( $"Testing {solution.Name} was successful" );
        }

        if ( settings.AnalyzeCoverage )
        {
            if ( !AnalyzeCoverageCommand.Execute(
                    context.Console,
                    new AnalyzeCoverageCommandSettings { Path = Path.Combine( testResultsDir, "coverage.net5.0.json" ) } ) )
            {
                return false;
            }
        }

        var testResultsDirectory = Path.Combine( context.RepoDirectory, product.TestResultsDirectory );

        if ( !Directory.Exists( testResultsDirectory ) )
        {
            Directory.CreateDirectory( testResultsDirectory );
        }

        if ( !Directory.GetFiles( testResultsDirectory ).Any() )
        {
            // We have to create an empty file, otherwise TeamCity will complain that
            // artifacts are missing.
            var emptyFile = Path.Combine( testResultsDirectory, ".empty" );

            File.WriteAllText( emptyFile, "This file is intentionally empty." );
        }

        // Raise the post-test event.
        var buildInfo = BuildArguments.Read( context, settings.BuildConfiguration );
        var privateArtifactsDirectory = product.GetPrivateArtifactsDirectory( context );
        var publicArtifactsDirectory = product.GetPublicArtifactsDirectory( context );

        var eventArgs = new BuildCompletedEventArgs( context, settings, buildInfo, privateArtifactsDirectory, publicArtifactsDirectory );
        product.OnTestCompleted( eventArgs );

        context.Console.WriteSuccess( $"Testing {product.ProductName} was successful" );

        return true;
    }
}