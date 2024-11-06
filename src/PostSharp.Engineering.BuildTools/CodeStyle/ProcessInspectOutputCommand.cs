// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.CodeStyle;

internal sealed class ProcessInspectOutputCommand : BaseCommand<ProcessInspectOutputCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, ProcessInspectOutputCommandSettings settings )
    {
        return ExecuteImpl( context, settings );
    }

    public static bool ExecuteImpl( BuildContext context, ProcessInspectOutputCommandSettings settings )
    {
        var lineMap = new Dictionary<string, List<int>>();

        var xml = XDocument.Load( settings.Path );

        var issueTypes =
            xml.Root!.Element( "IssueTypes" )!
                .Elements()
                .Select( e => new { Severity = e.Attribute( "Severity" )!.Value, Id = e.Attribute( "Id" )!.Value } )
                .ToDictionary( x => x.Id );

        foreach ( var issue in xml.Root.Element( "Issues" )!.Elements( "Project" ).SelectMany( x => x.Elements( "Issue" ) ) )
        {
            var issueTypeId = issue.Attribute( "TypeId" )!.Value;
            var issueType = issueTypes[issueTypeId];

            var rootPath = settings.SolutionDirectory != null
                ? Path.IsPathRooted( settings.SolutionDirectory )
                    ? settings.SolutionDirectory
                    : Path.Combine( context.RepoDirectory, settings.SolutionDirectory )
                : context.RepoDirectory;

            var line = issue.Attribute( "Line" ) is { Value: var value } ? int.Parse( value, CultureInfo.InvariantCulture ) : 1;
            var offsets = issue.Attribute( "Offset" )!.Value.Split( '-' );
            var file = Path.GetFullPath( Path.Combine( rootPath, issue.Attribute( "File" )!.Value ) );
            var offset = int.Parse( offsets[0], CultureInfo.InvariantCulture );
            var column = GetColumn( file, line, offset );

            var message =
                $"{file}({line},{column}): {issueType.Severity.ToLower( CultureInfo.InvariantCulture )}: {issue.Attribute( "Message" )!.Value} ({issueTypeId})";

            switch ( issueType.Severity )
            {
                case "WARNING":
                    context.Console.WriteWarning( message );

                    break;

                case "ERROR":
                    context.Console.WriteError( message );

                    break;
            }
        }

        return true;

        int GetColumn( string file, int line, int offset )
        {
            var map = GetLineMap( file );
            var lineOffset = map[line - 1];
            var column = offset - lineOffset;

            return column;
        }

        List<int> GetLineMap( string file )
        {
            if ( !lineMap.TryGetValue( file, out var map ) )
            {
                // Create the map.
                var text = File.ReadAllText( file );

                map = [0];

                for ( var offset = 0; offset < text.Length; offset++ )
                {
                    var c = text[offset];

                    if ( c == '\r' )
                    {
                        map.Add( offset + 1 );

                        if ( text.Length > offset && text[offset + 1] == '\n' )
                        {
                            offset++;
                        }
                    }
                    else if ( c == '\n' )
                    {
                        map.Add( offset + 1 );
                    }
                }

                lineMap[file] = map;
            }

            return map;
        }
    }
}