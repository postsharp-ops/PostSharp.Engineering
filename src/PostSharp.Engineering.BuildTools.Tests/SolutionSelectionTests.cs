// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using System.IO;
using System.Linq;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The solutions that the <c>build</c> and <c>test</c> commands process, according to the <c>--solution</c> and <c>--to</c> options.
/// </summary>
public sealed class SolutionSelectionTests
{
    private static readonly Product _product = new( MetalamaDependencies.V2026_1.Metalama )
    {
        Solutions =
        [
            new DotNetSolution( Path.Combine( "Core", "Core.sln" ) ) { Alias = "C" },
            new DotNetSolution( Path.Combine( "Patterns", "Patterns.sln" ) ) { Alias = "P" },
            new DotNetSolution( Path.Combine( "Tests", "Tests.sln" ) )
        ]
    };

    private static string[]? Select( BuildSettings settings, Product? product = null )
        => SolutionSelection.TryGetSolutions( TestBuildContext.Create( Path.GetTempPath(), product ?? _product ), settings, out var solutions )
            ? solutions.Select( s => Path.GetFileNameWithoutExtension( s.SolutionPath ) ).ToArray()
            : null;

    [Fact]
    public void WithoutOptionSelectsEverySolution() => Assert.Equal( ["Core", "Patterns", "Tests"], Select( new BuildSettings() ) );

    [Theory]
    [InlineData( "2" )]
    [InlineData( "P" )]
    [InlineData( "p" )]
    [InlineData( "Patterns" )]
    [InlineData( "patterns" )]
    [InlineData( "Patterns.sln" )]
    public void ToSelectsTheSolutionsUpToTheNamedOne( string value )
        => Assert.Equal( ["Core", "Patterns"], Select( new BuildSettings { LastSolution = value } ) );

    [Theory]
    [InlineData( "0" )]
    [InlineData( "4" )]
    [InlineData( "Unknown" )]
    [InlineData( "Patterns/Patterns.sln" )]
    public void ToRejectsAnUnknownSolution( string value ) => Assert.Null( Select( new BuildSettings { LastSolution = value } ) );

    [Theory]
    [InlineData( "2" )]
    [InlineData( "P" )]
    [InlineData( "Patterns" )]
    public void SolutionSelectsOneSolution( string value ) => Assert.Equal( ["Patterns"], Select( new BuildSettings { SolutionId = value } ) );

    [Fact]
    public void SolutionRejectsAnIdOutOfRange() => Assert.Null( Select( new BuildSettings { SolutionId = "4" } ) );

    [Fact]
    public void SolutionAndToAreExclusive() => Assert.Null( Select( new BuildSettings { SolutionId = "1", LastSolution = "Patterns" } ) );

    // A solution can override Solution.Name with a display name, as DocFxSolutionBase does; its file name still designates it.
    [Theory]
    [InlineData( "Reference.Site" )]
    [InlineData( "Reference" )]
    [InlineData( "Reference.Site:build" )]
    public void ToMatchesTheFileNameOfASolutionWithADisplayName( string value )
    {
        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            Solutions = [new DotNetSolution( Path.Combine( "Core", "Core.sln" ) ), new DisplayNameSolution( Path.Combine( "Docs", "Reference.Site" ) )]
        };

        Assert.Equal( ["Core", "Reference"], Select( new BuildSettings { LastSolution = value }, product ) );
    }

    private sealed class DisplayNameSolution : DotNetSolution
    {
        public DisplayNameSolution( string solutionPath ) : base( solutionPath ) { }

        public override string Name => Path.GetFileName( this.SolutionPath ) + ":build";
    }
}