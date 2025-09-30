// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Utilities;

public static class XDocumentHelper
{
    public static string ToNiceString( this XDocument document )
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ", // two spaces
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = true,
        };

        using var sw = new StringWriter();

        using ( var writer = XmlWriter.Create( sw, settings ) )
        {
            document.WriteTo( writer );
        }

        return sw.ToString();
    }
}