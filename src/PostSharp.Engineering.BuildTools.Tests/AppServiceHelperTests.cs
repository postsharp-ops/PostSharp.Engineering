// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

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
}
