// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build;

/// <summary>
/// Settings of the <see cref="SignCommand"/>.
/// </summary>
[PublicAPI]
public class SignCommandSettings : BaseBuildSettings
{
    [Description( "Directory holding the files to sign. Defaults to the public artifacts directory of the build configuration." )]
    [CommandOption( "--directory" )]
    public string? Directory { get; init; }

    [Description( "File name pattern to sign in the directory, repeatable. Defaults to the package and extension kinds the sign service takes directly." )]
    [CommandOption( "--input" )]
    public string[]? Filters { get; init; }

    [Description(
        "Path of a text file listing the files to sign, one absolute path per line. Signs assemblies and executables, which the sign service cannot take directly." )]
    [CommandOption( "--list-file" )]
    public string? ListFile { get; init; }

    [Description( "Skip the signature verification of NuGet packages." )]
    [CommandOption( "--no-verify" )]
    public bool NoVerify { get; init; }

    [Description( "Paths of the files to sign. Each is signed the way its kind requires." )]
    [CommandArgument( 0, "[files]" )]
    public string[]? Files { get; init; }
}

/// <summary>
/// Signs build artifacts. The standard build signs what it produces on its own; this command is for a product whose
/// public build is a configuration of its own and therefore never runs that step.
/// </summary>
/// <remarks>
/// It is deliberately not a pass-through to SignClient. What has to stay in one place is not the invocation but the
/// decisions around it -- which credential is required, how a file the service cannot take directly is presented to
/// it, and what is verified afterwards -- so that changing how the product is signed is a change here and nowhere
/// else. <see cref="ArtifactSigner"/> holds those decisions and this command and <see cref="BuildCommand"/> share it.
/// </remarks>
internal class SignCommand : BaseCommand<SignCommandSettings>
{
    protected override bool ExecuteCore( BuildContext context, SignCommandSettings settings )
    {
        context.Console.WriteHeading( "Signing artifacts" );

        var explicitPaths = ImmutableArray<string>.Empty;

        if ( settings.ListFile != null )
        {
            if ( !File.Exists( settings.ListFile ) )
            {
                context.Console.WriteError( $"The list file does not exist: '{settings.ListFile}'." );

                return false;
            }

            explicitPaths = explicitPaths.AddRange( ReadPaths( File.ReadAllLines( settings.ListFile ) ) );
        }

        if ( settings.Files is { Length: > 0 } )
        {
            explicitPaths = explicitPaths.AddRange( ReadPaths( settings.Files ) );
        }

        if ( !explicitPaths.IsEmpty )
        {
            if ( settings.Directory != null || settings.Filters is { Length: > 0 } )
            {
                context.Console.WriteError( "Naming the files to sign cannot be combined with --directory or --input, which select them." );

                return false;
            }

            if ( !ArtifactSigner.TrySignPaths( context, explicitPaths, !settings.NoVerify ) )
            {
                return false;
            }
        }
        else
        {
            var directory = settings.Directory ?? context.Product.GetPublicArtifactsAbsoluteDirectory( context );

            var filters = settings.Filters is { Length: > 0 }
                ? ImmutableArray.Create( settings.Filters )
                : ArtifactSigner.DefaultFilters;

            if ( !ArtifactSigner.TrySign( context, directory, filters, !settings.NoVerify ) )
            {
                return false;
            }
        }

        context.Console.WriteSuccess( "Signing artifacts was successful." );

        return true;
    }

    /// <summary>
    /// Normalizes the paths a caller named, whether on the command line or in a list file. They are made absolute
    /// because the sign service is addressed by directory, and a relative path would resolve against the working
    /// directory of whichever build step happens to be running.
    /// </summary>
    private static ImmutableArray<string> ReadPaths( IEnumerable<string> lines )
        =>
        [
            .. lines.Select( line => line.Trim() )
                .Where( line => line.Length > 0 )
                .Select( Path.GetFullPath )
        ];
}