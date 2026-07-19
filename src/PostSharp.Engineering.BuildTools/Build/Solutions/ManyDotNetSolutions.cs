// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

/// <summary>
/// An implementation of <see cref="ManySolutions"/> that builds each scenario with <c>dotnet</c>.
/// </summary>
public class ManyDotNetSolutions : ManySolutions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ManyDotNetSolutions"/> class.
    /// </summary>
    /// <param name="directory">A directory.</param>
    public ManyDotNetSolutions( string directory ) : base( directory ) { }

    protected override TestableSolution CreateSolution( string projectPath, BuildMethod testMethod )
        => new DotNetSolution( projectPath ) { EnvironmentVariables = this.EnvironmentVariables, TestMethod = testMethod };
}
