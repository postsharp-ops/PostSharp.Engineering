// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using NuGet.Versioning;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System.IO;

namespace PostSharp.Engineering.BuildTools.CodeStyle;

internal abstract class ResharperCommand : BaseCommand<CommonCommandSettings>
{
    protected abstract string Title { get; }

    protected abstract string GetCommand( BuildContext context, Solution solution );

    protected virtual void OnSuccessfulExecution( BuildContext context, Solution solution ) { }

    protected override bool ExecuteCore( BuildContext context, CommonCommandSettings settings )
    {
        context.Console.WriteHeading( this.Title );

        var buildSettings = new BuildSettings();
        buildSettings.Initialize( context );

        foreach ( var solution in context.Product.Solutions )
        {
            if ( !solution.CanFormatCode )
            {
                continue;
            }

            // Before formatting, the solution must be built.
            if ( !solution.Build( context, buildSettings ) )
            {
                return false;
            }

            foreach ( var formattableSolution in solution.GetFormattableSolutions( context ) )
            {
                // Determine the current SDK.
                ToolInvocationHelper.InvokeTool(
                    context.Console,
                    "dotnet",
                    "--version",
                    Path.GetDirectoryName( formattableSolution.SolutionPath ),
                    out _,
                    out var sdkVersionString,
                    ToolInvocationOptions.Default with { Silent = true } );

                if ( !NuGetVersion.TryParse( sdkVersionString, out var sdkVersion ) )
                {
                    context.Console.WriteError( $"Cannot parse Sdk version '{sdkVersionString}'." );

                    return false;
                }

                var command = this.GetCommand( context, formattableSolution ).Trim();

                // Exclude user- and machine-specific layers.
                command += " --disable-settings-layers:\"GlobalAll;GlobalPerProduct;SolutionPersonal;ProjectPersonal\"";

                // Exclude .nuget directory to prevent formatting the code inside NuGet packages.
                command += " --exclude:\"**\\.nuget\\**\\*";

                if ( formattableSolution.FormatExclusions is { Length: > 0 } )
                {
                    command += $";{string.Join( ';', formattableSolution.FormatExclusions )}";
                }

                command += "\"";

                // This is to force the tool to use a specific version of the .NET SDK. It does not work without that.
                // ReSharper disable once StringLiteralTypo
                command += $" --dotnetcoresdk={sdkVersion.ToFullString()}";

                command += " --verbosity=WARN";

                if ( !DotNetTool.Resharper.Invoke( context, command ) )
                {
                    return false;
                }

                this.OnSuccessfulExecution( context, formattableSolution );
            }
        }

        context.Console.WriteSuccess( $"{this.Title} was successful." );

        return true;
    }
}