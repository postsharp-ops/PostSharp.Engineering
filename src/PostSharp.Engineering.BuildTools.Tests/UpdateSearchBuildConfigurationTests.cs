// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Search;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using MetalamaDependencies = PostSharp.Engineering.BuildTools.Dependencies.Definitions.MetalamaDependencies;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The search update runs on the agents the product's requirements select, so it has to run where the product's build
/// runs. When the product builds in a container, those agents have no .NET SDK of their own.
/// </summary>
public sealed class UpdateSearchBuildConfigurationTests
{
    private static IReadOnlyList<BuildStep> GetSteps( BuildAgentRequirements requirements ) => Generate( requirements ).BuildSteps!;

    private static TeamCityBuildConfiguration Generate( BuildAgentRequirements requirements )
    {
        using var directory = new TempDirectory();

        var extension = new UpdateSearchProductExtension( "https://search.example", "test-source", "entries", _ => null! );

        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            OverriddenBuildAgentRequirements = requirements, Extensions = [extension]
        };

        var configurations = new List<TeamCityBuildConfiguration>();
        Assert.True( extension.AddTeamcityBuildConfiguration( TestBuildContext.Create( directory.Path, product ), configurations ) );

        return Assert.Single( configurations );
    }

    /// <summary>
    /// Without a <see cref="DockerSpec"/>, the step called <c>Build.ps1</c> on a container host, which failed to find
    /// <c>dotnet</c>; the marketplace index then went unrefreshed for months behind builds reported as successful.
    /// </summary>
    [Fact]
    public void InAContainerizedProductTheStepRunsInTheProductImage()
    {
        var step = GetSteps( new ContainerRequirements( ContainerHostKind.Windows ) )
            .OfType<EngineeringCommandBuildStep>()
            .Single();

        Assert.Equal( "DockerBuild.ps1", step.ScriptPath );
        Assert.Contains( "-Script Build.ps1", step.ScriptArguments, StringComparison.Ordinal );
        Assert.Contains( "search update", step.ScriptArguments, StringComparison.Ordinal );
    }

    /// <summary>
    /// The step runs with <c>-NoBuildImage</c>, so the image has to be built by an earlier step of the same configuration.
    /// </summary>
    [Fact]
    public void InAContainerizedProductTheImageIsPreparedFirst()
    {
        using var writer = new StringWriter();
        Generate( new ContainerRequirements( ContainerHostKind.Windows ) ).GenerateTeamcityCode( writer );
        var code = writer.ToString();

        var prepareImage = code.IndexOf( "id = \"PrepareImage\"", StringComparison.Ordinal );
        var updateSearch = code.IndexOf( "id = \"UpdateSearch\"", StringComparison.Ordinal );

        Assert.True( prepareImage >= 0, "The configuration has no step that prepares the image." );
        Assert.True( prepareImage < updateSearch, "The image is prepared after the step that runs in it." );
    }

    [Fact]
    public void InAProductBuiltOnTheAgentTheStepRunsBuildPs1Directly()
    {
        var step = Assert.Single( GetSteps( BuildAgentRequirements.SelfHosted( "test" ) ).OfType<EngineeringCommandBuildStep>() );

        Assert.Equal( "Build.ps1", step.ScriptPath );
    }
}
