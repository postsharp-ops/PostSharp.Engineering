// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PostSharp.Engineering.BuildTools.Utilities
{
    public static class AzHelper
    {
        private const string _exe = "cmd";
        private const string _batch = "az.cmd";

        private static string? _cmdArgsFormat;

        private static bool TryFormatCmdArgs( ConsoleHelper console, string args, [MaybeNullWhen( false )] out string cmdArgs )
        {
            if ( _cmdArgsFormat == null )
            {
                var exe = "where";
                var whereArgs = _batch;

                if ( !ToolInvocationHelper.InvokeTool( console, exe, whereArgs, Environment.CurrentDirectory, out _, out var whereOutput ) )
                {
                    console.WriteError( $"Error executing {exe} {whereArgs}" );
                    console.WriteError( whereOutput );

                    cmdArgs = null;

                    return false;
                }

                _cmdArgsFormat = $"/c \"{whereOutput.Trim()}\" {{0}}";
            }

            cmdArgs = string.Format( CultureInfo.InvariantCulture, _cmdArgsFormat, args );

            return true;
        }

        public static bool Login( ConsoleHelper console, bool dry = false )
        {
            string azArgs;

            if ( DockerHelper.IsDockerBuild() )
            {
                var azureTenantId = Environment.GetEnvironmentVariable( EnvironmentVariableNames.AzureTenantId );
                var azureClientId = Environment.GetEnvironmentVariable( EnvironmentVariableNames.AzureClientId );
                var azureClientSecret = Environment.GetEnvironmentVariable( EnvironmentVariableNames.AzureClientSecret );

                if ( string.IsNullOrEmpty( azureTenantId ) || string.IsNullOrEmpty( azureClientId ) || string.IsNullOrEmpty( azureClientSecret ) )
                {
                    console.WriteWarning(
                        $"Cannot do `az login`: The environment variables {EnvironmentVariableNames.AzureTenantId}, {EnvironmentVariableNames.AzureClientId}, {EnvironmentVariableNames.AzureClientSecret} must be defined." );

                    return false;
                }

                azArgs =
                    $"login --service-principal --client-id {azureClientId} --password %{EnvironmentVariableNames.AzureClientSecret}% --tenant {azureTenantId}";
            }
            else
            {
                var identityUserName = Environment.GetEnvironmentVariable( EnvironmentVariableNames.AzIdentityUserName );

                if ( identityUserName == null )
                {
                    console.WriteImportantMessage(
                        $"{EnvironmentVariableNames.AzIdentityUserName} environment variable not set. If the authorization fails, set this variable to use managed user identity or call 'az login'." );

                    return true;
                }

                azArgs = $"login --identity --username {identityUserName}";
            }

            if ( !TryFormatCmdArgs( console, azArgs, out var cmdArgs ) )
            {
                return false;
            }

            if ( dry )
            {
                console.WriteImportantMessage( $"Dry run: {_exe} {cmdArgs}" );

                return true;
            }
            else
            {
                return ToolInvocationHelper.InvokeTool(
                    console,
                    _exe,
                    cmdArgs,
                    Environment.CurrentDirectory );
            }
        }

        public static bool Query( ConsoleHelper console, string args, bool dry, [MaybeNullWhen( false )] out string output )
        {
            if ( !Login( console, dry ) )
            {
                output = null;

                return false;
            }

            if ( !TryFormatCmdArgs( console, args, out var cmdArgs ) )
            {
                output = null;

                return false;
            }

            if ( dry )
            {
                console.WriteImportantMessage( $"Dry run: {_exe} {cmdArgs}" );

                output = "<dry>";

                return true;
            }
            else
            {
                if ( !ToolInvocationHelper.InvokeTool( console, _exe, cmdArgs, Environment.CurrentDirectory, out _, out output ) )
                {
                    console.WriteError( output );

                    return false;
                }

                return true;
            }
        }

        public static bool Run( ConsoleHelper console, string args, bool dry, ToolInvocationOptions? options = null )
        {
            if ( !Login( console, dry ) )
            {
                return false;
            }

            if ( !TryFormatCmdArgs( console, args, out var cmdArgs ) )
            {
                return false;
            }

            if ( dry )
            {
                console.WriteImportantMessage( $"Dry run: {_exe} {cmdArgs}" );

                return true;
            }
            else
            {
                return ToolInvocationHelper.InvokeTool( console, _exe, cmdArgs, Environment.CurrentDirectory, options: options );
            }
        }
    }
}