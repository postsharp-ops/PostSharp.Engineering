// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools;
using Spectre.Console.Cli;

namespace PostSharp.Engineering.DocFx;

[PublicAPI]
public static class DocFxAppExtensions
{
    public static void AddDocFxCommands( this EngineeringApp app, DocFxOptions? options = null )
    {
        options ??= new DocFxOptions();
        var data = new DocFxCommandData( app.Product, options );

        app.Configure( config =>
        {
            config.AddBranch(
                "docfx",
                branch =>
                {
                    branch.AddCommand<DocFxMetadataCommand>( "metadata" ).WithData( data );
                    branch.AddCommand<DocFxBuildCommand>( "build" ).WithData( data );
                } );
        } );
    }
}