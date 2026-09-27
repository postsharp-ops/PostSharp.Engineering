// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using System.IO;
using System.Text.Json;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The mode of <c>dotnet test</c> is chosen by <c>global.json</c> only, so the file that PostSharp.Engineering generates is
/// what makes <see cref="Product.TestRunner"/> take effect.
/// </summary>
public sealed class GlobalJsonTestRunnerTests
{
    private static JsonElement Write( TestRunner testRunner )
    {
        using var directory = new TempDirectory();

        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            DotNetSdkVersion = new DotNetSdkVersion( "10.0.100" ), TestRunner = testRunner
        };

        Assert.True( GlobalJsonFile.TryWrite( TestBuildContext.Create( directory.Path, product ) ) );

        // The file must remain valid JSON, which the section is spliced into.
        return JsonDocument.Parse( File.ReadAllText( Path.Combine( directory.Path, "global.json" ) ) ).RootElement.Clone();
    }

    [Fact]
    public void TheTestingPlatformIsTheRunnerOfDotNetTest()
    {
        var globalJson = Write( TestRunner.MicrosoftTestingPlatform );

        Assert.Equal( "Microsoft.Testing.Platform", globalJson.GetProperty( "test" ).GetProperty( "runner" ).GetString() );
        Assert.Equal( "10.0.100", globalJson.GetProperty( "sdk" ).GetProperty( "version" ).GetString() );
    }

    [Fact]
    public void VSTestNeedsNoTestSection()
    {
        Assert.False( Write( TestRunner.VSTest ).TryGetProperty( "test", out _ ) );
    }
}
