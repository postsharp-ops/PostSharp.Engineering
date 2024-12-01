// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;

namespace PostSharp.Engineering.BuildTools.Build.Publishers
{
    /// <summary>
    /// Publishes NuGet packages to any feed.
    /// </summary>
    public class NugetPublisher : ArtifactPublisher
    {
        private readonly string _source;
        private readonly string _apiKey;

        public NugetPublisher( Pattern files, string source, string apiKey ) : base( files )
        {
            this._source = source;
            this._apiKey = apiKey;
        }

        public override SuccessCode PublishFile(
            BuildContext context,
            PublishSettings settings,
            string file,
            BuildInfo buildInfo,
            BuildConfigurationInfo configuration )
        {
            var hasEnvironmentError = false;

            context.Console.WriteMessage( $"Publishing {file}." );

            var server = Environment.ExpandEnvironmentVariables( this._source );

            // Check if environment variables have been defined.
            if ( string.IsNullOrEmpty( server ) )
            {
                context.Console.WriteError( $"The {this._source} environment variable is not defined." );
                hasEnvironmentError = true;
            }

            var apiKey = Environment.ExpandEnvironmentVariables( this._apiKey );

            if ( string.IsNullOrEmpty( apiKey ) )
            {
                context.Console.WriteError( $"The {this._apiKey} environment variable is not defined." );
                hasEnvironmentError = true;
            }

            if ( hasEnvironmentError )
            {
                return SuccessCode.Fatal;
            }

            const string azEndpointsEnvironmentVariableName = "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS";

            if ( apiKey == "az" && Environment.GetEnvironmentVariable( azEndpointsEnvironmentVariableName ) == null )
            {
                context.Console.WriteImportantMessage(
                    $"{azEndpointsEnvironmentVariableName} environment variable not set. If the authorization fails, set this variable or sign in interactively. See https://github.com/microsoft/artifacts-credprovider#use for details." );
            }

            var exe = "dotnet";

            // Note that we don't expand the ApiKey environment variable so we don't expose passwords to logs.
            var args =
                $"nuget push {file} --source {server} --api-key {this._apiKey} --skip-duplicate";

            if ( settings.Dry )
            {
                context.Console.WriteImportantMessage( $"Dry run: {exe} {args}" );

                return SuccessCode.Success;
            }
            else
            {
                return ToolInvocationHelper.InvokeTool(
                    context.Console,
                    exe,
                    args,
                    Environment.CurrentDirectory )
                    ? SuccessCode.Success
                    : SuccessCode.Error;
            }
        }
    }
}