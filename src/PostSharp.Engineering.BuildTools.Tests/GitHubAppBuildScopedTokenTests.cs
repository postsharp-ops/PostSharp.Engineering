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

    private static string[] GetTargetRepositories(
        TeamCityBuildConfiguration buildConfiguration,
        params GitHubRepository[] additionalRepositories )
        => TeamCitySettingsFile.GetTargetRepositories(
                new ConsoleHelper(),
                (GitHubRepository) _consolidated.DependencyDefinition.VcsRepository,
                _consolidated.DependencyDefinition.EffectiveGitHubAppConnectionId!,
                buildConfiguration,
                [..additionalRepositories] )
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
    /// A product can push to a repository that is not one of its source dependencies. Such a repository is added to the
    /// token through <see cref="Product.AdditionalGitHubTokenRepositories"/>, on top of whatever the build checks out.
    /// </summary>
    [Fact]
    public void AdditionalTokenRepository_OfTheSameOrganization_IsAdded()
    {
        // A repository the build pushes to without checking it out, in the same organization as the product.
        var additional = new GitHubRepository( "Metalama.Vsx", "metalama" );

        Assert.Equal(
            ["Metalama.Consolidated", "Metalama.Vsx"],
            GetTargetRepositories( CreateBuildConfiguration( [] ), additional ) );
    }

    /// <summary>
    /// An additional repository already reached as a source dependency must not be listed twice.
    /// </summary>
    [Fact]
    public void AdditionalTokenRepository_ThatIsAlreadyASourceDependency_IsNotDuplicated()
    {
        var buildConfiguration = CreateBuildConfiguration( new ProductProperties( _consolidated ).SourceDependencies );
        var alreadyReached = new GitHubRepository( "Metalama.Samples", "metalama" );

        var targetRepositories = GetTargetRepositories( buildConfiguration, alreadyReached );

        Assert.Single( targetRepositories, "Metalama.Samples" );
    }

    /// <summary>
    /// A token is issued by a single connection, which serves one organization, so an additional repository of another
    /// organization cannot be covered and is left out with a warning rather than making the token unissuable.
    /// </summary>
    [Fact]
    public void AdditionalTokenRepository_OfAnotherOrganization_IsLeftOut()
    {
        var foreign = new GitHubRepository( "TimelessDotNetEngineer", "postsharp" );

        Assert.Equal( ["Metalama.Consolidated"], GetTargetRepositories( CreateBuildConfiguration( [] ), foreign ) );
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

    private static string GenerateCode( GitHubAppBuildScopedTokenSettings settings )
    {
        var buildConfiguration = CreateBuildConfiguration( [] );
        buildConfiguration.GitHubAppBuildScopedToken = settings;

        var writer = new StringWriter();
        buildConfiguration.GenerateTeamcityCode( writer );

        return writer.ToString();
    }

    /// <summary>
    /// A build configuration that does not override anything keeps the parameter every build step and build tool reads.
    /// </summary>
    [Fact]
    public void ParameterName_DefaultsToTheOrdinaryGitHubToken()
    {
        Assert.Equal( "env.GITHUB_TOKEN", GitHubAppBuildScopedTokenSettings.DefaultParameterName );

        Assert.Contains(
            """
            parameterName = "env.GITHUB_TOKEN"
            """,
            GenerateCode( new GitHubAppBuildScopedTokenSettings( GitHubAppConnections.Metalama, ["Metalama.Consolidated"] ) ),
            StringComparison.Ordinal );
    }

    /// <summary>
    /// A build configuration that runs under an identity of its own issues its single token from another connection and
    /// writes it to another parameter. Both must reach the generated Kotlin together.
    /// </summary>
    [Fact]
    public void OverriddenConnectionAndParameter_AreBothEmitted()
    {
        var code = GenerateCode(
            new GitHubAppBuildScopedTokenSettings(
                GitHubAppConnections.MetalamaAgent,
                ["Metalama.Consolidated"],
                "env.CLAUDE_GITHUB_TOKEN" ) );

        Assert.Contains(
            """
            gitHubAppBuildScopedToken {
                        parameterName = "env.CLAUDE_GITHUB_TOKEN"
                        connectionId = "%GITHUB_CONNECTION_METALAMA_AGENT%"
                        targetRepositories = "Metalama.Consolidated"
                    }
            """,
            code,
            StringComparison.Ordinal );

        // Exactly one token is issued, so the connection it replaces must not appear as well.
        Assert.DoesNotContain( GitHubAppConnections.Metalama, code, StringComparison.Ordinal );
        Assert.DoesNotContain( "env.GITHUB_TOKEN", code, StringComparison.Ordinal );
    }

    private static GitHubAppBuildScopedTokenSettings CreateSettings( TeamCityBuildConfiguration buildConfiguration )
        => TeamCitySettingsFile.CreateBuildScopedTokenSettings(
            new ConsoleHelper(),
            (GitHubRepository) _consolidated.DependencyDefinition.VcsRepository,
            _consolidated.DependencyDefinition.EffectiveGitHubAppConnectionId!,
            buildConfiguration,
            [] );

    /// <summary>
    /// A build configuration that declares no override inherits the connection of its repository and the ordinary
    /// parameter.
    /// </summary>
    [Fact]
    public void WithoutAnOverride_TheRepositoryConnectionAndDefaultParameterAreUsed()
    {
        var settings = CreateSettings( CreateBuildConfiguration( [] ) );

        Assert.Equal( GitHubAppConnections.Metalama, settings.ConnectionId );
        Assert.Equal( "env.GITHUB_TOKEN", settings.ParameterName );
    }

    /// <summary>
    /// A build configuration that acts under an identity of its own replaces both the connection and the parameter.
    /// </summary>
    [Fact]
    public void AnOverride_ReplacesTheConnectionAndTheParameter()
    {
        var buildConfiguration = CreateBuildConfiguration( [] );

        buildConfiguration.GitHubAppTokenOverride =
            new GitHubAppTokenOverride( GitHubAppConnections.MetalamaAgent, "env.CLAUDE_GITHUB_TOKEN" );

        var settings = CreateSettings( buildConfiguration );

        Assert.Equal( GitHubAppConnections.MetalamaAgent, settings.ConnectionId );
        Assert.Equal( "env.CLAUDE_GITHUB_TOKEN", settings.ParameterName );
    }

    /// <summary>
    /// The parameter is optional: an override that only changes the identity keeps the token in the variable that the
    /// build steps and the build tools read.
    /// </summary>
    [Fact]
    public void AnOverrideWithoutAParameter_KeepsTheDefaultParameter()
    {
        var buildConfiguration = CreateBuildConfiguration( [] );
        buildConfiguration.GitHubAppTokenOverride = new GitHubAppTokenOverride( GitHubAppConnections.MetalamaAgent );

        var settings = CreateSettings( buildConfiguration );

        Assert.Equal( GitHubAppConnections.MetalamaAgent, settings.ConnectionId );
        Assert.Equal( GitHubAppBuildScopedTokenSettings.DefaultParameterName, settings.ParameterName );
    }

    /// <summary>
    /// The override substitutes the identity of the token but not its scope: the repositories the build reaches are
    /// still derived from the connection of the repository, which serves the same organization as the overriding
    /// connection. Deriving them from the override would compare it against the connection of every source dependency,
    /// match none, and silently narrow the token to the owning repository.
    /// </summary>
    [Fact]
    public void AnOverride_DoesNotNarrowTheTargetRepositories()
    {
        var buildConfiguration = CreateBuildConfiguration( new ProductProperties( _consolidated ).SourceDependencies );

        buildConfiguration.GitHubAppTokenOverride =
            new GitHubAppTokenOverride( GitHubAppConnections.MetalamaAgent, "env.CLAUDE_GITHUB_TOKEN" );

        // The full set of source dependencies survives the override.
        Assert.Contains( "Metalama.Compiler", CreateSettings( buildConfiguration ).TargetRepositories );

        // Guards the reason: scoping by the overriding connection instead would strip everything but the repository.
        var scopedByTheOverride = TeamCitySettingsFile.GetTargetRepositories(
                new ConsoleHelper(),
                (GitHubRepository) _consolidated.DependencyDefinition.VcsRepository,
                GitHubAppConnections.MetalamaAgent,
                buildConfiguration,
                [] )
            .ToArray();

        Assert.Equal( ["Metalama.Consolidated"], scopedByTheOverride );
    }
}
