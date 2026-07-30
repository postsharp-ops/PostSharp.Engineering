// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using MetalamaDependencies = PostSharp.Engineering.BuildTools.Dependencies.Definitions.MetalamaDependencies;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// An additional build configuration publishes nothing unless it says otherwise, which is right for one whose whole
/// result is its exit code and wrong for one that writes a file somebody will want afterwards.
/// </summary>
/// <remarks>
/// The case that prompted this: the nightly CEIP triage job transcribes its deterministic half to
/// <c>artifacts/preflight.log</c>, for the agent that runs next in the same container. That file dies with the
/// container. When a run reported the transcript as empty while the build log plainly showed the same commands
/// producing output, neither claim could be checked, because there was nothing left to look at.
/// </remarks>
public sealed class AdditionalConfigurationArtifactsTests
{
    private static readonly Product _product = new( MetalamaDependencies.V2026_1.Metalama );

    private static string GenerateCode( string[]? artifactRules )
    {
        var configuration = new PowershellAdditionalCiBuildConfiguration( "Nightly", "Nightly job", "Run.ps1", "-Something" )
        {
            ArtifactRules = artifactRules
        };

        var teamCityConfiguration = configuration.TeamCityBuildConfiguration(
            new ProductProperties( _product ),
            new Dictionary<BuildConfiguration, PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.TeamCityBuildConfiguration>() );

        var writer = new StringWriter();
        teamCityConfiguration.GenerateTeamcityCode( writer );

        return writer.ToString();
    }

    [Fact]
    public void AConfigurationThatDeclaresNothingPublishesNothing()
        => Assert.DoesNotContain( "artifactRules", GenerateCode( null ), StringComparison.Ordinal );

    [Fact]
    public void ADeclaredRuleReachesTheGeneratedConfiguration()
        => Assert.Contains( "artifactRules = \"\"\"+:artifacts/preflight.log\"\"\"", GenerateCode( ["+:artifacts/preflight.log"] ), StringComparison.Ordinal );

    /// <summary>
    /// Several rules are one Kotlin string separated by real newlines. Assembling them with a literal <c>\n</c> in
    /// the emitted text would leave the second rule outside the string and the settings file would not compile.
    /// </summary>
    [Fact]
    public void SeveralRulesAreOneStringSeparatedByRealNewlines()
    {
        var code = GenerateCode( ["+:artifacts/preflight.log", "+:artifacts/logs/**/*=>claude-logs"] );

        Assert.Contains(
            "artifactRules = \"\"\"+:artifacts/preflight.log\n+:artifacts/logs/**/*=>claude-logs\"\"\"",
            code,
            StringComparison.Ordinal );

        Assert.DoesNotContain( @"preflight.log\n+", code, StringComparison.Ordinal );
    }
}
