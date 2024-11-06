// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Dependencies;

/// <summary>
/// Updates configuration of all dependencies in the version file.
/// </summary>
[UsedImplicitly]
public class UpdateDependencyCommand : BaseFetchDependencyCommand
{
    protected override bool Update => true;
}