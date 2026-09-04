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
        //
        // No --user, and that is the substantive part. With it, SignClient uses the resource owner password
        // flow and signs in as sign-caravela@postsharp.net, whose password was SIGNSERVER_SECRET. That
        // account existed because the sign service reached the signing key on behalf of the calling user,
        // so the caller had to be a user. It no longer does: it reaches the key with its own identity and
        // authorizes the caller by an application role instead. Without --user, SignClient uses the client
        // credentials flow and presents the build agent's own service principal, whose token carries that
        // role rather than a delegated scope.
        //
        // The agent credential is the one already in the environment as AZURE_CLIENT_ID and
        // AZURE_CLIENT_SECRET, so signing no longer needs a TeamCity parameter of its own and the password
        // of a named user account stops being a build secret. The client id lives in
        // signclient-appsettings.json rather than here.

        command +=
            $" --config $(ToolsDirectory){Path.DirectorySeparatorChar}signclient-appsettings.json --name {context.Product.ProductName} --secret %AZURE_CLIENT_SECRET%";

        return base.Invoke( context, command, options );
    }
}