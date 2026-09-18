// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Collections.Generic;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Utilities;

[PublicAPI]
public static class TestLicenseKeyDownloader
{
    public static bool Download( BuildContext context, BuildSettings settings )
    {
        const string keyVaultUri = "https://testserviceskeyvault.vault.azure.net/";

        if ( BuildContext.IsGuestDevice )
        {
            context.Console.WriteWarning( "Skipping fetching of test license keys. Some licensing tests are going to fail." );

            return true;
        }

        // Should the content of the file change, change the file name, to keep older builds consistent.
        var testLicensesCacheDirectory = PathHelper.GetEngineeringDataDirectory();
        var licensesFile = Path.Combine( testLicensesCacheDirectory, "TestLicenseKeys1.g.props" );

        if ( File.Exists( licensesFile ) )
        {
            context.Console.WriteMessage( "Test license keys are already fetched." );

            return true;
        }

        var azureTenantId = Environment.GetEnvironmentVariable( EnvironmentVariableNames.AzureTenantId );
        var azureClientId = Environment.GetEnvironmentVariable( EnvironmentVariableNames.AzureClientId );
        var azureClientSecret = Environment.GetEnvironmentVariable( EnvironmentVariableNames.AzureClientSecret );

        // A service principal in the environment authenticates through the SDK, which needs no Azure CLI. That is
        // what a container has: the CLI is a large installation to carry in an image whose only use for it would be
        // to log in, and the three variables that the login would read are already there. A machine without them
        // falls back to the ambient session, which is what a developer has and what 'az login' establishes.
        var hasServicePrincipal = !string.IsNullOrEmpty( azureTenantId )
                                  && !string.IsNullOrEmpty( azureClientId )
                                  && !string.IsNullOrEmpty( azureClientSecret );

        if ( !hasServicePrincipal && !AzHelper.Login( context ) )
        {
            if ( context.IsContinuousIntegrationBuild )
            {
                context.Console.WriteError( "Cannot download test license keys." );
            }

            return false;
        }

        if ( !Directory.Exists( testLicensesCacheDirectory ) )
        {
            Directory.CreateDirectory( testLicensesCacheDirectory );
        }

        context.Console.WriteHeading( "Fetching test license keys." );
        context.Console.WriteMessage( "This operation can be lengthy, but its result is cached, and next time it won't need to be performed." );

        TokenCredential credential;

        if ( hasServicePrincipal )
        {
            // Named explicitly rather than left to the chain of DefaultAzureCredential, so that the container fails
            // with the error of this credential instead of the last of a dozen.
            credential = new ClientSecretCredential( azureTenantId, azureClientId, azureClientSecret );
        }
        else
        {
            var o = new DefaultAzureCredentialOptions()
            {
                // We se the tenant explicitly, to avoid issues where the user is logged in to various tenants at the same time. 
                VisualStudioTenantId = azureTenantId
            };

            credential = new DefaultAzureCredential( o );
        }

        var keyVault = new SecretClient( new Uri( keyVaultUri ), credential );

        var lines = new List<string>();

        lines.Add( "<Project>" );
        lines.Add( "  <PropertyGroup>" );

        var licenseKeyNames = new[]
        {
            "PostSharpEssentials",
            "PostSharpFramework",
            "PostSharpUltimate",
            "PostSharpEnterprise",
            "PostSharpUltimateOpenSourceRedistribution",
            "MetalamaFreePersonal",
            "MetalamaFreeBusiness",
            "MetalamaStarterPersonal",
            "MetalamaStarterBusiness",
            "MetalamaProfessionalPersonal",
            "MetalamaProfessionalBusiness",
            "MetalamaUltimatePersonal",
            "MetalamaUltimateBusiness",
            "MetalamaUltimateBusinessNotAuditable",
            "MetalamaUltimateOpenSourceRedistribution",
            "MetalamaUltimateCommercialRedistribution",
            "MetalamaUltimatePersonalProjectBound",
            "MetalamaUltimateOpenSourceRedistributionForIntegrationTests"
        };

        foreach ( var licenseKeyName in licenseKeyNames )
        {
            string licenseKey;

            try
            {
                licenseKey = keyVault.GetSecret( $"TestLicenseKey{licenseKeyName}" ).Value.Value;
            }
            catch ( Exception ex )
            {
                context.Console.WriteError( $"Could not get license key '{licenseKeyName}'." );
                context.Console.WriteMessage( ex.Message );

                return false;
            }

            lines.Add( $"    <{licenseKeyName}LicenseKey>{licenseKey}</{licenseKeyName}LicenseKey>" );
        }

        lines.Add( "  </PropertyGroup>" );
        lines.Add( "</Project>" );

        File.WriteAllLines( licensesFile, lines );

        context.Console.WriteMessage( "Test license keys fetched successfully." );

        return true;
    }
}