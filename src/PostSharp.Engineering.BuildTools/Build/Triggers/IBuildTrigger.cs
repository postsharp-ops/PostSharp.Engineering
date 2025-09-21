// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.IO;

namespace PostSharp.Engineering.BuildTools.Build.Triggers;

/// <summary>
/// A build trigger is an event or condition that causes the current product to be built. Build triggers are configured and scheduled
/// by the build server. Implementations of this interface must generate the proper TeamCity script.
/// </summary>
public interface IBuildTrigger
{
    /// <summary>
    /// Generates the TeamCity code representing the current build trigger.
    /// </summary>
    void GenerateTeamcityCode( TextWriter writer, string? branchFilter = null );
}