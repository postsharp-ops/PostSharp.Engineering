// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using System.Linq;
using Xunit;
using MetalamaDependencies = PostSharp.Engineering.BuildTools.Dependencies.Definitions.MetalamaDependencies;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The build-scoped token is fine-grained: it only reaches the repositories listed in <c>targetRepositories</c>. A build
/// that checks out its source dependencies and pushes to them (<c>Orchestrator.ps1 bump</c> and friends) therefore needs
/// all of them listed, not just the repository that owns the build.
/// </summary>
public class GitHubAppBuildScopedTokenTests
{
    private static readonly Product _consolidated = new( MetalamaDependencies.V2026_1.Consolidated );

    private static TeamCityBuildConfiguration CreateBuildConfiguration( TeamCitySourceDependency[] sourceDependencies )
        => new(
            "Bump",
            "Bump Versions",
            _consolidated.DependencyDefinition.Branch,
            "SomeVcsId",
            BuildAgentRequirements.Default ) { BuildSteps = [], SourceDependencies = sourceDependencies };

    private static string[] GetTargetRepositories( TeamCityBuildConfiguration buildConfiguration )
        => TeamCitySettingsFile.GetTargetRepositories(
                new ConsoleHelper(),
                (GitHubRepository) _consolidated.DependencyDefinition.VcsRepository,
                _consolidated.DependencyDefinition.EffectiveGitHubAppConnectionId!,
                buildConfiguration )
            .ToArray();

    /// <summary>
    /// This is the regression: the Bump build of Metalama.Consolidated walks into every <c>source-dependencies/*</c> and
    /// pushes a version bump to it, but the token used to be scoped to Metalama.Consolidated alone, so every push was
    /// rejected with a 403.
    /// </summary>
    [Fact]
    public void BuildThatChecksOutSourceDependencies_GetsATokenForAllOfThem()
    {
        var buildConfiguration = CreateBuildConfiguration( new ProductProperties( _consolidated ).SourceDependencies );

        Assert.Equal(
            [
                // The repository that owns the build comes first, then the source dependencies it checks out.
                "Metalama.Consolidated",
                "Metalama.Compiler",
                "Metalama",
                "Metalama.Community",
                "Metalama.Premium",
                "Metalama.Samples",
                "Metalama.Documentation",
                "Metalama.Tests.NopCommerce"
            ],
            GetTargetRepositories( buildConfiguration ) );
    }

    /// <summary>
    /// A build without source dependencies reaches nothing but its own repository, so its token must not be widened.
    /// </summary>
    [Fact]
    public void BuildWithoutSourceDependencies_GetsATokenForItsOwnRepositoryOnly()
        => Assert.Equal( ["Metalama.Consolidated"], GetTargetRepositories( CreateBuildConfiguration( [] ) ) );

    /// <summary>
    /// A token is issued by a single GitHub App connection, and a connection only serves the repositories of its own
    /// organization. A source dependency of another organization cannot be covered, so it is left out rather than making
    /// the whole token unissuable.
    /// </summary>
    [Fact]
    public void SourceDependencyOfAnotherOrganization_IsLeftOut()
    {
        // TimelessDotNetEngineer belongs to the Metalama family but to the 'postsharp' organization.
        var foreignDependency = MetalamaDependencies.V2026_1.TimelessDotNetEngineer;

        Assert.NotEqual(
            _consolidated.DependencyDefinition.EffectiveGitHubAppConnectionId,
            foreignDependency.EffectiveGitHubAppConnectionId );

        var buildConfiguration = CreateBuildConfiguration(
            [new TeamCitySourceDependency( foreignDependency, "+:. => source-dependencies/TimelessDotNetEngineer" )] );

        Assert.Equal( ["Metalama.Consolidated"], GetTargetRepositories( buildConfiguration ) );
    }

    /// <summary>
    /// <c>targetRepositories</c> takes a newline-separated list, and TeamCity has no token standing for all repositories,
    /// so the repositories the build reaches are enumerated, separated by the Kotlin escape sequence for a newline. A
    /// build that reaches a single repository therefore keeps the plain form it had before.
    /// </summary>
    [Fact]
    public void RepositoriesAreEmittedAsANewlineSeparatedKotlinString()
    {
        Assert.Contains(
            """
            targetRepositories = "Metalama.Consolidated\nMetalama.Compiler\nMetalama\nMetalama.Community\nMetalama.Premium\nMetalama.Samples\nMetalama.Documentation\nMetalama.Tests.NopCommerce"
            """,
            GenerateCode( new ProductProperties( _consolidated ).SourceDependencies ),
            StringComparison.Ordinal );

        Assert.Contains( "targetRepositories = \"Metalama.Consolidated\"", GenerateCode( [] ), StringComparison.Ordinal );

        string GenerateCode( TeamCitySourceDependency[] sourceDependencies )
        {
            var buildConfiguration = CreateBuildConfiguration( sourceDependencies );

            buildConfiguration.GitHubAppBuildScopedToken = new GitHubAppBuildScopedTokenSettings(
                GitHubAppConnections.Metalama,
                [..GetTargetRepositories( buildConfiguration )] );

            var writer = new StringWriter();
            buildConfiguration.GenerateTeamcityCode( writer );

            return writer.ToString();
        }
    }
}
