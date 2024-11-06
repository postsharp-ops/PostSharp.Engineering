// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

[UsedImplicitly]
public class GenerateCiScriptsCommand : BaseCommand<CommonCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, CommonCommandSettings settings )
        => context.Product.IsBundle
            ? TeamCityHelper.TryGenerateConsolidatedTeamcityConfiguration( context )
            : context.Product.GenerateTeamcityConfiguration( context, settings );
}