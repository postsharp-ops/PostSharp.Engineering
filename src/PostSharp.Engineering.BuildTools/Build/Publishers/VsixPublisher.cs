// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;

namespace PostSharp.Engineering.BuildTools.Build.Publishers
{
    /// <summary>
    /// Publishes VSIX packages to Visual Studio Marketplace. 
    /// </summary>
    public class VsixPublisher : ArtifactPublisher
    {
        public VsixPublisher( Pattern files ) : base( files )
        {
            // We don't publish pre-release VSIX by default because Visual Studio Marketplace supports only a single
            // version and we don't want to replace a release with a pre-release.
            this.PublishPrerelease = false;
        }

        public override SuccessCode PublishFile(
            BuildContext context,
            PublishSettings settings,
            string file,
            BuildInfo buildInfo,
            BuildConfigurationInfo configuration )
        {
            var hasEnvironmentError = false;

            if ( string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "VSSDKINSTALL" ) ) )
            {
                context.Console.WriteError( $"The VSSDKINSTALL environment variable is not defined." );
                hasEnvironmentError = true;
            }

            if ( string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "VS_MARKETPLACE_ACCESS_TOKEN" ) ) )
            {
                context.Console.WriteError( $"The VS_MARKETPLACE_ACCESS_TOKEN environment variable is not defined." );
                hasEnvironmentError = true;
            }

            if ( hasEnvironmentError )
            {
                return SuccessCode.Fatal;
            }

            var vsSdkDir = Environment.GetEnvironmentVariable( "VSSDKINSTALL" );

            var exe = $@"{vsSdkDir}\VisualStudioIntegration\Tools\Bin\VsixPublisher.exe";

            var args =
                $" publish -payload \"{file}\" -publishManifest \"{file}.json\" -personalAccessToken \"%VS_MARKETPLACE_ACCESS_TOKEN%\"";

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