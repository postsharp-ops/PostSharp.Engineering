// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Build.Model;

/// <summary>
/// The test platform of a product, which is the mode of <c>dotnet test</c> in its repository. See
/// <c>doc/testing-platform.md</c>.
/// </summary>
[PublicAPI]
public enum TestRunner
{
    /// <summary>
    /// VSTest: <c>dotnet test</c> in its default mode, with the TRX logger.
    /// </summary>
    VSTest,

    /// <summary>
    /// Microsoft.Testing.Platform. The <c>global.json</c> that PostSharp.Engineering generates selects the mode of
    /// <c>dotnet test</c> that runs test applications, such as those of xunit.v3 or of the runner of MSTest, and
    /// <c>Build.ps1 test</c> passes the options of that mode. The mode applies to every <c>dotnet test</c> run from the
    /// repository root; a directory that must keep VSTest has a <c>global.json</c> of its own.
    /// </summary>
    MicrosoftTestingPlatform
}
