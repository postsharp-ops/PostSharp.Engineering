// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

/// <summary>
/// The command that a <see cref="TestableSolution"/> must translate into a command line of its build engine.
/// </summary>
[PublicAPI]
public enum SolutionCommand
{
    Restore,
    Build,
    Test,
    Pack
}
