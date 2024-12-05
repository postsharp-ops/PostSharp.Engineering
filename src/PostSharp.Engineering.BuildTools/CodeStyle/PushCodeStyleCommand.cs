// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Utilities;
using System.IO;

namespace PostSharp.Engineering.BuildTools.CodeStyle
{
    /// <summary>
    /// Copies the code style from the current repo to the shared repo to. 
    /// </summary>
    [UsedImplicitly]
    internal class PushCodeStyleCommand : BaseCodeStyleCommand<CodeStyleSettings>
    {
        protected override bool ExecuteCore( BuildContext context, CodeStyleSettings settings )
        {
            context.Console.WriteHeading( "Pushing code style." );

            var sharedRepo = GetCodeStyleRepo( context, settings );

            if ( sharedRepo == null )
            {
                return false;
            }

            // Copy the files (removing the previous content).
            context.Console.WriteImportantMessage( $"Copying '{context.Product.EngineeringDirectory}' to '{sharedRepo}'." );

            CopyDirectory(
                Path.Combine( context.RepoDirectory, context.Product.EngineeringDirectory, "style" ),
                sharedRepo );

            // Stage all changes to the commit, but does not commit.
            ToolInvocationHelper.InvokeTool( context.Console, "git", $"add --all", sharedRepo );

            ToolInvocationHelper.InvokeTool( context.Console, "git", $"status", sharedRepo );

            context.Console.WriteSuccess( "Pushing code style was successful. You now need to commit and push manually." );

            return true;
        }
    }
}