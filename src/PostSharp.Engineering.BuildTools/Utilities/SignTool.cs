// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;

namespace PostSharp.Engineering.BuildTools.Utilities;

internal class SignTool : DotNetTool
{
    private const string _configurationFileName = "signclient-appsettings.json";

    // These three values identify the sign service itself, not the caller, so they have no counterpart in the
    // environment. AADInstance is the Entra authority. ResourceId must match the 'AzureAd:Audience' of the
    // server, which is why it is a URI and not a registration identifier.
    private const string _aadInstance = "https://login.microsoftonline.com/";
    private const string _serviceUrl = "https://signservice.postsharp.net";
    private const string _serviceResourceId = "https://SignService/d4e6f350-201e-4744-9a19-c413e8f0c565";

    public SignTool() : base( "sign", "SignClient", "1.3.155", "SignClient" ) { }

    public override bool Invoke( BuildContext context, string command, ToolInvocationOptions? options = null )
    {
        // No --user, and that is the substantive part. With it, SignClient uses the resource owner password
        // flow and signs in as sign-caravela@postsharp.net, whose password was SIGNSERVER_SECRET. That
        // account existed because the sign service reached the signing key on behalf of the calling user,
        // so the caller had to be a user. It no longer does: it reaches the key with its own identity and
        // authorizes the caller by an application role instead. Without --user, SignClient uses the client
        // credentials flow and presents the build agent's own service principal, whose token carries that
        // role rather than a delegated scope.
        //
        // The agent credential is the one already in the environment as AZURE_CLIENT_ID and
        // AZURE_CLIENT_SECRET, so signing no longer needs a TeamCity parameter of its own and the password
        // of a named user account stops being a build secret.

        if ( !TryWriteConfigurationFile( context, out var configurationFilePath ) )
        {
            return false;
        }

        // We don't pass the secret so it does not get printed. We pass an environment variable reference instead.
        // The ToolInvocationHelper will expand it.
        command += $" --config \"{configurationFilePath}\" --name {context.Product.ProductName} --secret %{EnvironmentVariableNames.AzureClientSecret}%";

        return base.Invoke( context, command, options );
    }

    /// <summary>
    /// Writes the SignClient configuration file from the current environment. The client and tenant identifiers
    /// are read from the environment rather than stored in the repository so that they always designate the same
    /// service principal as the secret they are used with. A hardcoded client identifier would silently stop
    /// matching AZURE_CLIENT_SECRET as soon as the build agent credential changed.
    /// </summary>
    private static bool TryWriteConfigurationFile( BuildContext context, [NotNullWhen( true )] out string? configurationFilePath )
    {
        configurationFilePath = null;

        if ( !TryGetRequiredEnvironmentVariable( context, EnvironmentVariableNames.AzureClientId, out var clientId )
             || !TryGetRequiredEnvironmentVariable( context, EnvironmentVariableNames.AzureTenantId, out var tenantId ) )
        {
            return false;
        }

        var configuration = new
        {
            SignClient = new
            {
                AzureAd = new { AADInstance = _aadInstance, ClientId = clientId, TenantId = tenantId },
                Service = new { Url = _serviceUrl, ResourceId = _serviceResourceId }
            }
        };

        var toolsDirectory = GetToolsDirectory( context );
        Directory.CreateDirectory( toolsDirectory );

        var path = Path.Combine( toolsDirectory, _configurationFileName );

        File.WriteAllText( path, JsonSerializer.Serialize( configuration, new JsonSerializerOptions { WriteIndented = true } ) );

        context.Console.WriteMessage( $"Generated '{path}' for client id '{clientId}'." );

        configurationFilePath = path;

        return true;
    }

    private static bool TryGetRequiredEnvironmentVariable( BuildContext context, string name, [NotNullWhen( true )] out string? value )
    {
        value = Environment.GetEnvironmentVariable( name );

        if ( string.IsNullOrEmpty( value ) )
        {
            context.Console.WriteError( $"The {name} environment variable is not defined." );

            return false;
        }

        return true;
    }
}
