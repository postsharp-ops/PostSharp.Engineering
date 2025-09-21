// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Dependencies;

[UsedImplicitly]
internal class FetchDependencyCommand : BaseFetchDependencyCommand
{
    protected override bool Update => false;
}