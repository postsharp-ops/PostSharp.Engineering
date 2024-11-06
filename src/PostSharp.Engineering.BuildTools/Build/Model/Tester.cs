// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Build.Model;

public abstract class Tester
{
    public abstract SuccessCode Execute(
        BuildContext context,
        string artifactsDirectory,
        BuildInfo buildInfo,
        bool dry );
}