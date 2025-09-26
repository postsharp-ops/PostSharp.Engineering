// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Publishing.Downloads;

[PublicAPI]
public record DownloadIndex( DownloadFolder Folder, string? Name, bool IsPartialIndex )
{
    public string FileName => this.Name == null ? "Index.xml" : $"Index.{this.Name}.xml";

    public void Write( string directory )
    {
        static string FormatTime( DateTime time ) => time.ToString( @"yyyy-MM-ddTHH\:mm\:ss.fffffffzzz", CultureInfo.InvariantCulture );

        static string FormatString( string? value )
            => value == null
                ? ""
                : $@"<![CDATA[
            {value}
          ]]>";

        var createdAt = FormatTime( this.Folder.CreatedAt );
        var partialIndex = this.IsPartialIndex ? @" PartialIndex=""true""" : "";

        var files = string.Join(
            Environment.NewLine,
            this.Folder.Files.Select( f => $@"<File Name=""{f.Name}"" CreatedAt=""{FormatTime( f.CreatedAt )}"">
        <Description>
          {FormatString( f.Description )}
        </Description>
        <Instructions>
          {FormatString( f.Instructions )}
        </Instructions>
      </File>" ) );

        var content = @$"<?xml version=""1.0"" encoding=""utf-8"" ?>
<Index xmlns=""http://www.postsharp.net/schemas/downloads.xsd"">
  <Folder Name=""{this.Folder.Name}"" Order=""{this.Folder.Order}"" CreatedAt=""{createdAt}""{partialIndex}>
    <Description>
      {FormatString( this.Folder.Description )}
    </Description>
    <LongDescription>
      {FormatString( this.Folder.LongDescription )}
    </LongDescription>
    <Instructions>
      {FormatString( this.Folder.Instructions )}
    </Instructions>
    <Files>
      {files}
    </Files>
  </Folder>

</Index>
";

        var indexFilePath = Path.Combine( directory, this.FileName );

        File.WriteAllText( indexFilePath, content );
    }
}