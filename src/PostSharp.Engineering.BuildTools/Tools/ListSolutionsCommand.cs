// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using Spectre.Console;
using System.Globalization;

namespace PostSharp.Engineering.BuildTools.Tools;

[UsedImplicitly]
internal sealed class ListSolutionsCommand : BaseCommand<CommonCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, CommonCommandSettings settings )
    {
        var table = new Table();
        table.AddColumn( "Id" );
        table.AddColumn( "Solution" );
        table.AddColumn( "Type" );

        var i = 0;

        foreach ( var solution in context.Product.Solutions )
        {
            i++;
            table.AddRow( i.ToString( CultureInfo.InvariantCulture ), solution.SolutionPath, solution.GetType().Name );
        }

        context.Console.Write( table );

        return true;
    }
}