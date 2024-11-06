// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.BuildTools.Git;

[UsedImplicitly]
internal class UpstreamCheckCommand : BaseCommand<UpstreamCheckSettings>
{
    protected override bool ExecuteCore( BuildContext context, UpstreamCheckSettings settings ) => DownstreamMerge.CheckUpstreamChanges( context, settings );
}