// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using System.IO;

namespace PostSharp.Engineering.BuildTools.CodeStyle;

[UsedImplicitly]
internal class InspectCommand : ResharperCommand
{
    protected override string Title => "Inspecting the code";

    protected override string GetCommand( BuildContext context, Solution solution )
    {
        var outputPath = Path.Combine( context.RepoDirectory, "artifacts", "logs", "CodeIssues.xml" );

        context.Console.WriteImportantMessage( $"Writing result to '{outputPath}'." );

        return
            $"inspectcode \"{Path.Combine( context.RepoDirectory, solution.SolutionFilterPathForInspectCode ?? solution.SolutionPath )}\" --build -o={outputPath} --severity=WARNING -f=Xml";
    }

    protected override void OnSuccessfulExecution( BuildContext context, Solution solution )
    {
        var outputPath = Path.Combine( context.RepoDirectory, "artifacts", "logs", "CodeIssues.xml" );

        var solutionDirectory = Path.GetDirectoryName( solution.SolutionPath );

        ProcessInspectOutputCommand.ExecuteImpl(
            context,
            new ProcessInspectOutputCommandSettings() { Path = outputPath, SolutionDirectory = solutionDirectory } );
    }
}