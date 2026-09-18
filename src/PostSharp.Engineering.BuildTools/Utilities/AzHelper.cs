// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PostSharp.Engineering.BuildTools.Utilities
{
    /// <summary>
    /// Runs the Azure CLI. Everything here goes through <c>cmd /c az.cmd</c> and is therefore Windows only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// That is deliberate rather than an oversight. What is left here is publishing and deployment -
    /// <see cref="AppServiceHelper"/>, <c>AzureDevOpsHelper</c> and <c>MsDeployPublisher</c> - which run on Windows
    /// agents and which need the command line tool, because they issue real commands rather than merely
    /// authenticate. The one caller that had to work elsewhere was <see cref="TestLicenseKeyDownloader"/>, which runs
    /// in a Linux container and now authenticates through the Azure SDK instead, so that the image does not have to
    /// carry the command line tool for the sake of a login.
    /// </para>
    /// <para>
    /// Porting this to Linux would mean more than calling <c>az</c> instead of <c>cmd</c>. The service principal
    /// login below passes the secret as <c>%AZURE_CLIENT_SECRET%</c>, which is expanded by cmd and therefore keeps
    /// the secret out of the argument list; there is no equivalent that does not either name the secret in the
    /// command line or introduce a shell. Do it when something actually needs it.
    /// </para>
    /// </remarks>
    public static class AzHelper
    {
        private const string _exe = "cmd";
        private const string _batch = "az.cmd";

        private static string? _cmdArgsFormat;

        // `az login` persists its session for the whole process, so it needs to run only once even though every
        // AzHelper.Query/Run calls Login first. Mirrors GitHelper._credentialsConfigured. Never reset: a process
        // authenticates once and keeps that session for its lifetime.
        private static bool _isLoggedIn;

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

        public static bool Login( BuildContext context, bool dry = false )
        {
            // Already authenticated for the lifetime of this process; the az session is reused.
            if ( _isLoggedIn )
            {
                return true;
            }

            string azArgs;

            var console = context.Console;

            if ( context.IsRunningUnderContainer )
            {
                // In a development build, we expect the following environment variables to be exported from the PostSharpBuildEnv key vault from the host,
                // by DockerBuild.ps1, and exported to the container.
                // In a CI build, we expect these environment variables to be set on the host and exported to the container, also by DockerBuild.ps1. 
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
                    $"login --service-principal --username {azureClientId} --password %{EnvironmentVariableNames.AzureClientSecret}% --tenant {azureTenantId}";
            }
            else
            {
                var identityUserName = Environment.GetEnvironmentVariable( EnvironmentVariableNames.AzIdentityUserName );

                if ( identityUserName == null )
                {
                    console.WriteImportantMessage(
                        $"{EnvironmentVariableNames.AzIdentityUserName} environment variable not set. If the authorization fails, set this variable to use managed user identity or call 'az login'." );

                    // There is no login command to run here; we rely on the ambient az session. Remember it (except
                    // on a dry run) so this message is not repeated before every az command.
                    if ( !dry )
                    {
                        _isLoggedIn = true;
                    }

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

                // A dry run does not authenticate, so it must not mark the process as logged in.
                return true;
            }
            else
            {
                if ( !ToolInvocationHelper.InvokeTool(
                        console,
                        _exe,
                        cmdArgs,
                        Environment.CurrentDirectory ) )
                {
                    return false;
                }

                console.WriteSuccess( "`az login` was successful." );

                _isLoggedIn = true;

                return true;
            }
        }

        public static bool Query( BuildContext context, string args, bool dry, [MaybeNullWhen( false )] out string output )
        {
            var console = context.Console;

            if ( !Login( context, dry ) )
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

        public static bool Run( BuildContext context, string args, bool dry, ToolInvocationOptions? options = null )
        {
            var console = context.Console;

            if ( !Login( context, dry ) )
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