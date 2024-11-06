// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Build;

[UsedImplicitly]
public class PrepareCommand : BaseCommand<BuildSettings>
{
    protected override bool ExecuteCore( BuildContext context, BuildSettings settings )
        => context.Product.Prepare( context, settings );
}