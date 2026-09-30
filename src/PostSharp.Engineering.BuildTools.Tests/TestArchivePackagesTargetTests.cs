// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Testing;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// The restore graph names the target of a .NET Framework application by the moniker of the framework, and the target of a
/// .NET 5 or later application by the short name of the framework. <see cref="TestArchivePackages"/> accepts both, so that it
/// finds the packages of a .NET Framework application. Before, it found none, and <c>Build.ps1 build</c> reported every
/// package of the archive that is not from nuget.org as a package that no build configuration downloads.
/// </summary>
public sealed class TestArchivePackagesTargetTests
{
    [Theory]
    [InlineData( "net8.0", "net8.0" )]
    [InlineData( "net8.0/win-x64", "net8.0" )]
    [InlineData( ".NETFramework,Version=v4.8", ".NETFramework,Version=v4.8" )]
    [InlineData( ".NETFramework,Version=v4.8/win-x86", ".NETFramework,Version=v4.8" )]
    public void TheTargetOfTheFrameworkIsFound( string targetName, string framework )
        => Assert.True( TestArchivePackages.IsTargetOf( targetName, framework ) );

    [Theory]
    [InlineData( ".NETFramework,Version=v4.8", "net48" )]
    [InlineData( "net8.0", "net8.0-windows" )]
    [InlineData( "net80", "net8.0" )]
    [InlineData( "net8.0", "" )]
    public void TheTargetOfAnotherFrameworkIsNotFound( string targetName, string framework )
        => Assert.False( TestArchivePackages.IsTargetOf( targetName, framework ) );
}
