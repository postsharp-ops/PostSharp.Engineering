// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools;

public enum ExitCodes
{
    Success,
    Error,
    Exception = 100,
    Cancelled = 200,
    Timeout = 300
}