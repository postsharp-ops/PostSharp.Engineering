// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Docker;
using System.Collections.Generic;

namespace PostSharp.Engineering.BuildTools.Build.Testing;

public abstract class Tester : IBuildComponent
{
    public abstract SuccessCode Execute(
        BuildContext context,
        string artifactsDirectory,
        BuildArguments buildArguments,
        bool dry );

    public virtual bool VerifyContainerRequirements( BuildContext context, ContainerRequirements requirements ) => true;

    IEnumerable<IBuildComponent> IBuildComponent.Children => [];
}