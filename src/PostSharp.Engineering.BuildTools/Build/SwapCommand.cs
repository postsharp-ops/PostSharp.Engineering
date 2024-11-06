// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Build;

/// <summary>
/// Swaps two deployment slots.
/// </summary>
[UsedImplicitly]
public class SwapCommand : BaseCommand<SwapSettings>
{
    protected override bool ExecuteCore( BuildContext context, SwapSettings settings ) => context.Product.Swap( context, settings );
}