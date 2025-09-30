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
    Error,

    /// <summary>
    /// No change was made.
    /// </summary>
    NoChangeMade,

    /// <summary>
    /// Unhandled exception.
    /// </summary>
    Exception = 100,

    /// <summary>
    /// Cancelled through Ctrl+C.
    /// </summary>
    Cancelled = 200
}