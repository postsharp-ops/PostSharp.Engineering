// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PostSharp.Engineering.BuildTools.Build.Publishers
{
    /// <summary>
    /// A <see cref="Publisher"/> that uses <c>MSDeploy</c> to deploy a web site.
    /// </summary>
    public class MsDeployPublisher : ArtifactPublisher
    {
        private readonly ImmutableArray<MsDeployConfiguration> _configurations;

        public MsDeployPublisher( IReadOnlyCollection<MsDeployConfiguration> configurations )
            : base( Pattern.Create( configurations.Select( c => c.PackageFileName ).ToArray() ) )
        {
            this._configurations = ImmutableArray.Create<MsDeployConfiguration>().AddRange( configurations );
        }

        private static bool QueryPublishProfile(
            BuildContext context,
            PublishSettings settings,
            MsDeployConfiguration configuration,
            [MaybeNullWhen( false )] out PublishProfile publishProfile )
        {
            var args =
                $"webapp deployment list-publishing-profiles --subscription {configuration.SubscriptionId} --resource-group {configuration.ResourceGroupName} --name {configuration.SiteName} --slot {configuration.SlotName}";

            if ( !AzHelper.Query( context.Console, args, settings.Dry, out var profiles ) )
            {
                publishProfile = null;

                return false;
            }

            if ( settings.Dry )
            {
                profiles = _dryPublishProfiles;
            }

            var profilesJson = JsonDocument.Parse( profiles );
            var msDeployProfileJson = profilesJson.RootElement.EnumerateArray().Single( e => e.GetProperty( "publishMethod" ).GetString() == "MSDeploy" );

            publishProfile = new PublishProfile(
                PublishUrl: msDeployProfileJson.GetProperty( "publishUrl" ).GetString()!,
                UserName: msDeployProfileJson.GetProperty( "userName" ).GetString()!,
                Password: msDeployProfileJson.GetProperty( "userPWD" ).GetString()! );

            return true;
        }

        public override SuccessCode PublishFile(
            BuildContext context,
            PublishSettings settings,
            string file,
            BuildInfo buildInfo,
            BuildConfigurationInfo configuration )
        {
            var fileName = Path.GetFileName( file );
            var packageConfiguration = this._configurations.Single( c => c.PackageFileName.ToString( buildInfo ) == fileName );

            if ( !QueryPublishProfile( context, settings, packageConfiguration, out var publishProfile ) )
            {
                return SuccessCode.Error;
            }

            context.Console.WriteMessage( $"Publishing {file} to {publishProfile.PublishUrl}{packageConfiguration.VirtualDirectory}." );

            var exe = @"C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe";

            var iisWebApplicationName = packageConfiguration.VirtualDirectory == null
                ? packageConfiguration.SiteName
                : $"{packageConfiguration.SiteName}{packageConfiguration.VirtualDirectory}";

            // The arguments are taken from the log of the [Azure DevOps] [Azure App Service deploy] [release pipeline] step.
            var argsList = new List<string>
            {
                "-verb:sync",
                $"-source:package='{file}'",
                $"-dest:auto,ComputerName='https://{publishProfile.PublishUrl}/msdeploy.axd?site={packageConfiguration.SiteName}',UserName='{publishProfile.UserName}',Password='$(Password)',AuthType='Basic'",
                $"-setParam:name='IIS Web Application Name',value='{iisWebApplicationName}'",
                "-enableRule:AppOffline",
                "-retryAttempts:6",
                "-retryInterval:10000"
            };

            if ( packageConfiguration.VirtualDirectory != null && !packageConfiguration.VirtualDirectory.StartsWith( '/' ) )
            {
                throw new InvalidOperationException( "The virtual directory has to start with a forward slash ('/')." );
            }

            var args = string.Join( ' ', argsList );

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
                    args.Replace( "$(Password)", publishProfile.Password, StringComparison.Ordinal ),
                    Environment.CurrentDirectory )
                    ? SuccessCode.Success
                    : SuccessCode.Error;
            }
        }

        private record PublishProfile(
            string PublishUrl,
            string UserName,
            string Password );

#pragma warning disable SA1203 // Constants should appear before fields
        private const string _dryPublishProfiles = @"[
  {
    ""SQLServerDBConnectionString"": """",
    ""controlPanelLink"": ""http://windows.azure.com"",
    ""databases"": null,
    ""destinationAppUrl"": ""http://dry-web-staging.azurewebsites.net"",
    ""hostingProviderForumLink"": """",
    ""msdeploySite"": ""dry-web__staging"",
    ""mySQLDBConnectionString"": """",
    ""profileName"": ""dry-web-staging - Web Deploy"",
    ""publishMethod"": ""MSDeploy"",
    ""publishUrl"": ""dry-web-staging.scm.azurewebsites.net:443"",
    ""userName"": ""$dry-web__staging"",
    ""userPWD"": ""youcanttouchthis"",
    ""webSystem"": ""WebSites""
  },
  {
    ""SQLServerDBConnectionString"": """",
    ""controlPanelLink"": ""http://windows.azure.com"",
    ""databases"": null,
    ""destinationAppUrl"": ""http://dry-web-staging.azurewebsites.net"",
    ""ftpPassiveMode"": ""True"",
    ""hostingProviderForumLink"": """",
    ""mySQLDBConnectionString"": """",
    ""profileName"": ""dry-web-staging - FTP"",
    ""publishMethod"": ""FTP"",
    ""publishUrl"": ""ftp://dry.ftp.azurewebsites.windows.net/site/wwwroot"",
    ""userName"": ""dry-web__staging\\$dry-web__staging"",
    ""userPWD"": ""youcanttouchthis"",
    ""webSystem"": ""WebSites""
  },
  {
    ""SQLServerDBConnectionString"": """",
    ""controlPanelLink"": ""http://windows.azure.com"",
    ""databases"": null,
    ""destinationAppUrl"": ""http://dry-web-staging.azurewebsites.net"",
    ""hostingProviderForumLink"": """",
    ""mySQLDBConnectionString"": """",
    ""profileName"": ""dry-web-staging - Zip Deploy"",
    ""publishMethod"": ""ZipDeploy"",
    ""publishUrl"": ""dry-web-staging.scm.azurewebsites.net:443"",
    ""userName"": ""$dry-web__staging"",
    ""userPWD"": ""youcanttouchthis"",
    ""webSystem"": ""WebSites""
  },
  {
    ""SQLServerDBConnectionString"": """",
    ""controlPanelLink"": ""http://windows.azure.com"",
    ""databases"": null,
    ""destinationAppUrl"": ""http://dry-web-staging.azurewebsites.net"",
    ""ftpPassiveMode"": ""True"",
    ""hostingProviderForumLink"": """",
    ""mySQLDBConnectionString"": """",
    ""profileName"": ""dry-web-staging - ReadOnly - FTP"",
    ""publishMethod"": ""FTP"",
    ""publishUrl"": ""ftp://drydr.ftp.azurewebsites.windows.net/site/wwwroot"",
    ""userName"": ""dry-web__staging\\$dry-web__staging"",
    ""userPWD"": ""youcanttouchthis"",
    ""webSystem"": ""WebSites""
  }
]"
#pragma warning restore SA1203 // Constants should appear before fields
            ;
    }
}