// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

public enum SourceDependenciesRequirements
{
    None,
    Full,

    [Obsolete( "This does not work (not compatible with agent-side checkout)." )]
    EngOnly
}