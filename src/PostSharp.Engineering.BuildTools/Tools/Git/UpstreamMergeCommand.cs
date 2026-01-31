// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.BuildTools.Tools.Git;

[UsedImplicitly]
internal class UpstreamMergeCommand : BaseCommand<UpstreamMergeSettings>
{
    protected override bool ExecuteCore( BuildContext context, UpstreamMergeSettings settings ) => UpstreamMerge.MergeUpstream( context, settings );
}