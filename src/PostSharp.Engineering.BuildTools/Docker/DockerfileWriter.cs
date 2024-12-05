// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using System;
using System.Collections.Generic;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public abstract class DockerfileWriter : IDisposable
{
    public TextWriter TextWriter { get; }

    public DockerImage Image { get; }

    protected DockerfileWriter( TextWriter streamTextWriter, DockerImage image )
    {
        this.TextWriter = streamTextWriter;
        this.Image = image;
    }

    public virtual string EscapePath( string s ) => "\"" + s.Replace( "\\", "\\\\", StringComparison.Ordinal ) + "\"";

    public virtual void WritePrologue( BuildSettings settings )
    {
        this.WriteLine( $"ENV NUGET_PACKAGES={this.EscapePath( this.Image.NuGetPackagesDirectory )}" );
        this.WriteLine( $"ENV ENG_USERNAME={settings.UserName}" );

        ConfigureVolume( this.Image.NuGetPackagesDirectory );
        ConfigureVolume( this.Image.DownloadedBuildArtifactsDirectory );

        void ConfigureVolume( string directory )
        {
            this.WriteLine( $"VOLUME {this.EscapePath( directory )}" );
        }
    }

    public void WriteLine( string s ) => this.TextWriter.WriteLine( s );

    public void Dispose() => this.TextWriter.Dispose();

    public void Close() => this.TextWriter.Close();

    public abstract void MakeDirectory( string s );

    public abstract void ReplaceLink( string target, string alias );

    public abstract void RunPowerShellFile( string fileAndArguments );

    public abstract void Rename( string oldName, string newName );

    public abstract void RunPowerShellScript( List<string> commands );
}