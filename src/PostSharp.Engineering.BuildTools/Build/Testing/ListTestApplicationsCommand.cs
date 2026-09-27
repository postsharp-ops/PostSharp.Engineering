// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build.Testing;

/// <summary>
/// Lists the Microsoft.Testing.Platform test applications that <c>generate-scripts</c> plans the test build configurations
/// from, as <see cref="TestApplicationDiscovery"/> finds them.
/// </summary>
[UsedImplicitly]
internal sealed class ListTestApplicationsCommand : BaseCommand<CommonCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, CommonCommandSettings settings )
    {
        // The applications are listed even when two of them collide, because the list is what shows the collision.
        var success = TestApplicationDiscovery.TryDiscover( context, BuildConfiguration.Debug, out var applications );

        if ( applications.IsDefault )
        {
            return false;
        }

        var table = new Table();
        table.AddColumn( "Archive" );
        table.AddColumn( "Project" );
        table.AddColumn( "Platforms" );
        table.AddColumn( "Tags" );
        table.AddColumn( "Run alone" );
        table.AddColumn( "Skip" );

        foreach ( var application in applications )
        {
            table.AddRow(
                Markup.Escape( application.ArchiveName ),
                Markup.Escape( Path.GetRelativePath( context.RepoDirectory, application.ProjectPath ) ),
                Markup.Escape( string.Join( ", ", application.Platforms ) ),
                Markup.Escape( string.Join( ", ", application.Tags ) ),
                application.RunAlone ? "yes" : "",
                Markup.Escape( application.Skip ?? "" ) );
        }

        context.Console.Write( table );

        return success;
    }
}
