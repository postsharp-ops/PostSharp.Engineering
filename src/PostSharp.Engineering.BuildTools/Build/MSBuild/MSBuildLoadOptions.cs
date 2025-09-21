// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;

namespace PostSharp.Engineering.BuildTools.Build.MSBuild;

public static class MSBuildLoadOptions
{
    public static ProjectOptions IgnoreImportErrors { get; } = new()
    {
        LoadSettings = ProjectLoadSettings.IgnoreEmptyImports | ProjectLoadSettings.IgnoreInvalidImports | ProjectLoadSettings.IgnoreMissingImports
    };
}