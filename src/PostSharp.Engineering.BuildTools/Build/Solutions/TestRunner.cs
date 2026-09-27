// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

/// <summary>
/// The way <c>Build.ps1 test</c> runs the tests of a <see cref="DotNetSolution"/> or of a <see cref="MsbuildSolution"/>.
/// </summary>
[PublicAPI]
public enum TestRunner
{
    /// <summary>
    /// <c>dotnet test</c> for a <see cref="DotNetSolution"/>, and the <c>Test</c> target of the solution for a
    /// <see cref="MsbuildSolution"/>.
    /// </summary>
    Default,

    /// <summary>
    /// The test projects are Microsoft.Testing.Platform test applications. Each application that the build has written is
    /// run with the <c>InvokeTestingPlatform</c> target of <c>Microsoft.Testing.Platform.MSBuild</c>, and its TRX report is
    /// imported into TeamCity. The mode of <c>dotnet test</c> is not involved: it is chosen by <c>global.json</c>, which
    /// applies to the whole repository. See <c>doc/testing-platform.md</c>.
    /// </summary>
    MicrosoftTestingPlatform
}
