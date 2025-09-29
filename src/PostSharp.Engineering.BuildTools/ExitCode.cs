// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools;

public enum ExitCode
{
    /// <summary>
    /// Success.
    /// </summary>
    Success,

    /// <summary>
    /// Handled error.
    /// </summary>
    Error = 1,

    /// <summary>
    /// No change was made.
    /// </summary>
    NoChangeMade = 2,

    /// <summary>
    /// Unhandled exception.
    /// </summary>
    Exception = 100,

    /// <summary>
    /// Cancelled through Ctrl+C.
    /// </summary>
    Cancelled = 200
}