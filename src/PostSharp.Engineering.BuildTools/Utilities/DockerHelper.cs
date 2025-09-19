// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools.Utilities;

internal static class DockerHelper
{
    public static bool IsDockerBuild()
    {
        var s = Environment.GetEnvironmentVariable( "RUNNING_IN_DOCKER" );

        return s != null && ((bool.TryParse( s, out var b ) && b) || (int.TryParse( s, out var i ) && i != 0));
    }
}