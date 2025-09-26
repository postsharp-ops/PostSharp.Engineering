// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Utilities;

internal class SignTool : DotNetTool
{
    public SignTool() : base( "sign", "SignClient", "1.3.155", "SignClient" ) { }

    public override bool Invoke( BuildContext context, string command, ToolInvocationOptions? options = null )
    {
        // We don't pass the secret so it does not get printed. We pass an environment variable reference instead.
        // The ToolInvocationHelper will expand it.

        command +=
            $" --config $(ToolsDirectory){Path.DirectorySeparatorChar}signclient-appsettings.json --name {context.Product.ProductName} --user sign-caravela@postsharp.net --secret %SIGNSERVER_SECRET%";

        return base.Invoke( context, command, options );
    }
}