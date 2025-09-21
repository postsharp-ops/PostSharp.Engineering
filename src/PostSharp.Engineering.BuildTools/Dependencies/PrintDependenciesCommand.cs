// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Dependencies;

/// <summary>
/// Prints the content of <c>Versions.g.props</c> to the console.
/// </summary>
[UsedImplicitly]
internal class PrintDependenciesCommand : BaseCommand<CommonCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, CommonCommandSettings settings )
    {
        var path = Path.Combine(
            context.RepoDirectory,
            DependenciesOverrideFile.GetPath(
                context,
                settings,
                ConfigurationNeutralVersionFile.ReadDefaultConfiguration( context ) ?? BuildConfiguration.Debug ) );

        if ( File.Exists( path ) )
        {
            context.Console.WriteImportantMessage( $"'{path}' has the following content:" );
            context.Console.WriteMessage( File.ReadAllText( path ) );
        }
        else
        {
            context.Console.WriteWarning( $"The file '{path}' does not exist. There are no local dependencies." );
        }

        return true;
    }
}