// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Utilities;
using System.IO;

namespace PostSharp.Engineering.BuildTools.CodeStyle
{
    /// <summary>
    /// Copies the code style from the shared repo to the current repo. 
    /// </summary>
    [UsedImplicitly]
    internal class PullCodeStyleCommand : BaseCodeStyleCommand<PullCodeStyleSettings>
    {
        protected override bool ExecuteCore( BuildContext context, PullCodeStyleSettings settings )
        {
            context.Console.WriteHeading( "Pulling code style" );

            var sharedRepo = GetCodeStyleRepo( context, settings );

            if ( sharedRepo == null )
            {
                return false;
            }

            var branch = settings.Branch ?? "master";

            // Check out master and pull.
            if ( !ToolInvocationHelper.InvokeTool( context.Console, "git", $"checkout {branch}", sharedRepo ) )
            {
                return false;
            }

            if ( !ToolInvocationHelper.InvokeTool( context.Console, "git", $"pull", sharedRepo ) )
            {
                return false;
            }

            if ( !GitHelper.CheckNoChange( context, settings, context.RepoDirectory ) )
            {
                return false;
            }

            var targetDirectory = Path.Combine( context.RepoDirectory, context.Product.EngineeringDirectory, "style" );

            CopyDirectory( sharedRepo, targetDirectory );

            // Create a symbolic link for .editorconfig.
            if ( !context.Product.KeepEditorConfig )
            {
                var editorConfigPath = Path.Combine( context.RepoDirectory, ".editorconfig" );
                var sharedEditorConfigPath = Path.Combine( context.Product.EngineeringDirectory, "style", ".editorconfig" );

                context.Console.WriteMessage( $"Creating link '{editorConfigPath}' => '{sharedEditorConfigPath}'." );

                if ( File.Exists( editorConfigPath ) )
                {
                    File.Delete( editorConfigPath );
                }

                File.CreateSymbolicLink( editorConfigPath, sharedEditorConfigPath );
            }

            context.Console.WriteSuccess( "Pulling code style was successful." );

            return true;
        }
    }
}