// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using NuGet.Versioning;
using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;

namespace PostSharp.Engineering.BuildTools.Utilities
{
    public class DotNetTool
    {
        public string PackageId { get; }

        public string Command { get; }

        public string Version { get; }

        public string Alias { get; }

        public static DotNetTool SignClient { get; } = new SignTool();

        public static DotNetTool Resharper { get; } = new( "jb", "JetBrains.Resharper.GlobalTools", "2025.2.0", "jb" );

        public static ImmutableArray<DotNetTool> DefaultTools { get; } = [SignClient, Resharper];

        [PublicAPI]
        public DotNetTool( string alias, string packageId, string version, string command )
        {
            this.Alias = alias;
            this.PackageId = packageId;
            this.Version = version;
            this.Command = command;
        }

        public bool Install( BuildContext context )
        {
            var baseDirectory = context.RepoDirectory;

            var configFilePath = Path.Combine( baseDirectory, ".config", "dotnet-tools.json" );
            var resourceDirectory = Path.Combine( baseDirectory, ".tools" );

            // 1. Create the dotnet tool manifest.
            if ( !File.Exists( configFilePath ) )
            {
                if ( !ToolInvocationHelper.InvokeTool(
                        context.Console,
                        "dotnet",
                        $"new tool-manifest",
                        baseDirectory ) )
                {
                    return false;
                }
            }

            // Open the config file and see if we have to install or update.
            string? installVerb = null;
            var configDocument = JsonDocument.Parse( File.ReadAllText( configFilePath ) );

            var installedVersionString = configDocument.RootElement.GetPropertyOrNull( "tools" )
                .GetPropertyOrNull( this.PackageId.ToLowerInvariant() )
                .GetPropertyOrNull( "version" )
                ?.GetString();

            if ( installedVersionString == null )
            {
                installVerb = "install";
            }
            else
            {
                var installedVersion = NuGetVersion.Parse( installedVersionString );

                if ( installedVersion < NuGetVersion.Parse( this.Version ) )
                {
                    installVerb = "update";
                }
            }

            // 2. Restore the tool.
            if ( installVerb != null )
            {
                if ( !ToolInvocationHelper.InvokeTool(
                        context.Console,
                        "dotnet",
                        $"tool {installVerb} {this.PackageId} --version {this.Version} --local --add-source \"https://api.nuget.org/v3/index.json\"",
                        baseDirectory ) )
                {
                    return false;
                }
            }

            // 3. Restore the tools from the manifest
            // The manifest might contain tools, that have been removed from the machine, or not yet installed.
            // The tools are stored in NuGet package cache, that can be cleaned.
            if ( !ToolInvocationHelper.InvokeTool(
                    context.Console,
                    "dotnet",
                    $"tool restore --add-source \"https://api.nuget.org/v3/index.json\"",
                    baseDirectory ) )
            {
                return false;
            }

            // 4. Restore resource tools.
            Directory.CreateDirectory( resourceDirectory );
            var assembly = this.GetType().Assembly;

            foreach ( var resourceName in assembly.GetManifestResourceNames() )
            {
                const string prefix = "PostSharp.Engineering.BuildTools.Resources.Tools.";

                if ( resourceName.StartsWith( prefix, StringComparison.Ordinal ) )
                {
                    using var resource = assembly.GetManifestResourceStream( resourceName );

                    var file = Path.Combine( resourceDirectory, resourceName.Substring( prefix.Length ) );

                    using ( var outputStream = File.Create( file ) )
                    {
                        resource!.CopyTo( outputStream );
                    }
                }
            }

            return true;
        }

        public virtual bool Invoke( BuildContext context, string command, ToolInvocationOptions? options = null )
        {
            if ( !this.Install( context ) )
            {
                return false;
            }

            var resourceDirectory = Path.Combine( context.RepoDirectory, ".tools" );

            command = command.Replace( "$(ToolsDirectory)", resourceDirectory, StringComparison.Ordinal );

            // 4. Invoke the tool.
            return ToolInvocationHelper.InvokeTool(
                context.Console,
                "dotnet",
                $"tool run {this.Command} {command}",
                context.RepoDirectory, // Must use the repo global.json (not the eng one) because that's the one used by Install.
                options );
        }
    }
}