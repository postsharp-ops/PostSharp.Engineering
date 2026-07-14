// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

public class GitHubAppConnectionTests
{
    // Enumerating the whole assembly would load types that reference Microsoft.Build, which the test host cannot
    // resolve, so we start from the definition classes and walk their nested version classes.
    private static readonly Type[] _definitionRoots =
    [
        typeof(DevelopmentDependencies), typeof(BusinessSystemsDependencies), typeof(TemplateDependencies),
        typeof(PostSharpDependencies), typeof(MetalamaDependencies), typeof(MetalamaVsxDependencies), typeof(TestDependencies)
    ];

    private static IEnumerable<(string Name, DependencyDefinition Definition)> GetAllDependencyDefinitions()
        => _definitionRoots
            .SelectMany( t => t.GetNestedTypes( BindingFlags.Public ).Append( t ) )
            .SelectMany( t => t.GetProperties( BindingFlags.Public | BindingFlags.Static ) )
            .Where( p => p.PropertyType.IsAssignableTo( typeof(DependencyDefinition) ) )
            .Select( p => ($"{p.DeclaringType!.Name}.{p.Name}", (DependencyDefinition?) p.GetValue( null )) )
            .Where( x => x.Item2 != null )
            .Select( x => (x.Item1, x.Item2!) );

    /// <summary>
    /// A GitHub App connection can only issue a token for the repositories of its own organization, so the connection
    /// of every repository must match its owner. This is not implied by the product family: the Metalama families own
    /// repositories of both organizations.
    /// </summary>
    [Fact]
    public void EveryGitHubRepository_UsesTheConnectionOfItsOwner()
    {
        var checkedCount = 0;

        foreach ( var (name, definition) in GetAllDependencyDefinitions() )
        {
            if ( definition.VcsRepository is not GitHubRepository repository )
            {
                continue;
            }

            var expected = repository.Owner.ToLowerInvariant() switch
            {
                "metalama" => GitHubAppConnections.Metalama,
                "postsharp" => GitHubAppConnections.PostSharp,
                "postsharp-ops" => GitHubAppConnections.PostSharpOps,
                _ => throw new InvalidOperationException( $"'{name}': unknown GitHub organization '{repository.Owner}'." )
            };

            Assert.Equal( expected, definition.EffectiveGitHubAppConnectionId );
            checkedCount++;
        }

        Assert.True( checkedCount > 20, $"Only {checkedCount} GitHub repositories were checked. The reflection sweep is probably broken." );
    }

    [Fact]
    public void PostSharpEngineering_UsesThePostSharpOpsConnection()
        => Assert.Equal( GitHubAppConnections.PostSharpOps, DevelopmentDependencies.PostSharpEngineering.EffectiveGitHubAppConnectionId );

    [Fact]
    public void MetalamaRepository_UsesTheMetalamaConnection()
        => Assert.Equal( GitHubAppConnections.Metalama, MetalamaDependencies.V2026_1.Metalama.EffectiveGitHubAppConnectionId );

    /// <summary>
    /// This repository belongs to the Metalama family but to the 'postsharp' organization, so the family default must
    /// be overridden.
    /// </summary>
    [Fact]
    public void PostSharpOwnedRepositoryOfMetalamaFamily_OverridesTheFamilyConnection()
    {
        Assert.Equal( GitHubAppConnections.Metalama, MetalamaDependencies.V2026_1.Family.GitHubAppConnectionId );

        Assert.Equal(
            GitHubAppConnections.PostSharp,
            MetalamaDependencies.V2026_1.TimelessDotNetEngineer.EffectiveGitHubAppConnectionId );

        Assert.Equal( GitHubAppConnections.PostSharp, MetalamaDependencies.V2026_1.NopCommerce.EffectiveGitHubAppConnectionId );
    }
}
