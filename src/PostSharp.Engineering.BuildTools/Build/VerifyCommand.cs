// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Build;

[UsedImplicitly]
public class VerifyCommand : BaseCommand<PublishSettings>
{
    protected override bool ExecuteCore( BuildContext context, PublishSettings settings )
        => context.Product.Verify( context, settings );
}