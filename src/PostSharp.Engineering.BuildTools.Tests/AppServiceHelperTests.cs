// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Publishing;
using PostSharp.Engineering.BuildTools.Utilities;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

public class AppServiceHelperTests
{
    [Fact]
    public void DeploymentSlot_IsPassed()
    {
        var args = AppServiceHelper.CreateArgs( "webapp stop", "sub", "rg", "site", "staging" );

        Assert.Equal( "webapp stop --subscription sub --resource-group rg --name site --slot staging", args );
    }

    // Azure has no slot named 'production': the production slot is the site itself, and `az` rejects `--slot production`.
    // Omitting the argument for any other slot name would stop the production site by mistake.
    [Theory]
    [InlineData( "production" )]
    [InlineData( "Production" )]
    [InlineData( null )]
    [InlineData( "" )]
    public void ProductionSlot_IsNotPassed( string? slotName )
    {
        var args = AppServiceHelper.CreateArgs( "webapp start", "sub", "rg", "site", slotName );

        Assert.Equal( "webapp start --subscription sub --resource-group rg --name site", args );
    }

    // The exception to the rule above, and the reason swapping does not go through CreateArgs: 'slot swap' is the one
    // command that names the production slot rather than addressing it by omission. Dropping it here would swap the
    // staging slot with whatever slot `az` defaulted to.
    [Fact]
    public void Swap_NamesTheProductionTargetSlot()
    {
        var args = AppServiceHelper.CreateSwapArgs( "sub", "rg", "site", "staging", AppServiceHelper.ProductionSlotName );

        Assert.Equal(
            "webapp deployment slot swap --subscription sub --resource-group rg --name site --slot staging --target-slot production",
            args );
    }

    [Fact]
    public void MsDeployPublisher_DoesNotSwapUnlessAsked()
    {
        var configuration = new MsDeployConfiguration( "site.zip", "sub", "rg", "site" );

        // The default has to stay false: a product that declares an AppServiceSwapper and gets an implicit swap here
        // as well would swap twice, and the second swap puts the previous build back into production.
        Assert.False( new MsDeployPublisher( [configuration] ).SwapAfterDeployment );

        // Whereas a swap does leave the slot running the old build against production's data, so stopping it is what
        // AppServiceSwapper does and what this defaults to.
        Assert.True( new MsDeployPublisher( [configuration] ).StopSlotAfterSwap );
    }
}
