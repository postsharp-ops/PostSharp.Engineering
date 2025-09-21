// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.DotNetTools;

[UsedImplicitly]
internal class InvokeDotNetToolCommand : BaseCommand<InvokeDotNetToolCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, InvokeDotNetToolCommandSettings settings )
    {
        var tool = context.Product.DotNetTools.SingleOrDefault( t => t.Alias == context.CommandContext.Name );

        if ( tool == null )
        {
            context.Console.WriteError( $"There is no tool named '{context.CommandContext.Name}'." );

            return false;
        }

        return tool.Invoke( context, settings.Arguments ?? "" );
    }
}