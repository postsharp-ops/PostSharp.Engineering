// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Docker;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.Build;

public interface IBuildComponent
{
    bool VerifyContainerRequirements( BuildContext context, ContainerRequirements requirements );

    IEnumerable<IBuildComponent> Children { get; }
}