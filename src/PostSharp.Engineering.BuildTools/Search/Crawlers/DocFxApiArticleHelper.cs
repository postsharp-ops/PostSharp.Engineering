// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using HtmlAgilityPack;
using System;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Search.Crawlers;

internal static class DocFxApiArticleHelper
{
    public static NextParagraphStrategy GetNextParagraphStrategy( HtmlNode paragraphInitialNode )
    {
        var nodeName = paragraphInitialNode.Name.ToLowerInvariant();

        if ( !Regex.IsMatch( nodeName, @"^h\d$" ) )
        {
            throw new InvalidOperationException( $"Expected h1-h6 instead of '{nodeName}'." );
        }

        var level = paragraphInitialNode.Name[1] - '0';

        bool isIgnored;

        string GetId() => paragraphInitialNode.GetAttributeValue<string>( "id", "" );

        switch ( level )
        {
            case 1: // For overview - eg: https://doc-production.metalama.net/api/metalama_framework_eligibility_describedobject-1
            case 4: // For method overloads - eg: https://doc-production.metalama.net/api/metalama_framework_eligibility_eligibilityextensions_mustbe
                isIgnored = IsMemberListItemId(
                    GetId() ); // Ignore list of members - eg: https://doc-production.metalama.net/api/metalama_framework_eligibility

                break;

            case 2: // For multi-paragraph description - eg: https://doc-production.metalama.net/api/metalama_framework_eligibility
                switch ( GetId() )
                {
                    case "conceptual-documentation":
                    case "overview":
                        isIgnored = false;

                        break;

                    default:
                        isIgnored = true;

                        break;
                }

                break;

            case 5:
                var text = paragraphInitialNode.GetText().Trim();
                isIgnored = text != "Remarks";

                break;

            default:
                isIgnored = true;

                break;
        }

        var isNextSnippet = !isIgnored && level == 4; // Next method overload.

        return new NextParagraphStrategy( isNextSnippet, isIgnored );
    }

    public static bool IsMemberListItemId( string id )
        => id.StartsWith(
            "ID",
            StringComparison.Ordinal );
}