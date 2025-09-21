// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console;

namespace PostSharp.Engineering.BuildTools.Build.MSBuild;

[UsedImplicitly]
internal class ListMSBuildCommand : BaseCommand<CommonCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, CommonCommandSettings settings )
    {
        var table = new Table();
        table.AddColumns( "Name", "Version", "Path", "Source" );

        // List instances discovered by MSBuildLocator.
        foreach ( var instance in MSBuildHelper.GetMSBuildInstances( context ) )
        {
            table.AddRow(
                instance.Name,
                instance.FullVersion,
                instance.Path,
                instance.Source );
        }

        context.Console.Write( table );

        context.Console.WriteMessage(
            $"MSBuildHelper.{nameof(MSBuildHelper.FindMSBuildExe)} returns: \"{MSBuildHelper.FindMSBuildExe( context ) ?? "<null>"}\"." );

        context.Console.WriteMessage( $"MSBuildLocator.RegisterDefault returned: \"{MSBuildHelper.RegisteredInstance?.MSBuildPath ?? "<null>"}\"." );

        return true;
    }
}