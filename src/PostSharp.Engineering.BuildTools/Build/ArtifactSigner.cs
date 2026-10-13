// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build;

/// <summary>
/// Signs build artifacts through the sign service. This is the single place that knows how the product is signed:
/// which credential is required, how SignClient is called, and how a file that SignClient cannot take directly is
/// presented to it.
/// </summary>
/// <remarks>
/// <para>
/// It exists as a service rather than as a block inside <see cref="BuildCommand"/> because not every product signs
/// from the standard build. A product whose public build is its own configuration -- one that assembles a
/// distribution from artifacts an earlier configuration produced -- signs from that build instead, through the
/// <c>sign</c> command, and must not have to reproduce any of this.
/// </para>
/// </remarks>
internal static class ArtifactSigner
{
    /// <summary>
    /// What a public build publishes and therefore signs by default.
    /// </summary>
    public static ImmutableArray<string> DefaultFilters { get; } = ["*.nupkg", "*.snupkg", "*.vsix"];

    /// <summary>
    /// Signs, in place, the files of <paramref name="directory"/> matching <paramref name="filters"/>, and verifies
    /// the signature of every NuGet package there unless <paramref name="verifyNuGetPackages"/> says otherwise.
    /// </summary>
    /// <remarks>
    /// A filter matching nothing is skipped rather than being an error: the set of filters describes what a product
    /// may produce, not what it must.
    /// </remarks>
    public static bool TrySign(
        BuildContext context,
        string directory,
        ImmutableArray<string> filters,
        bool verifyNuGetPackages = true,
        ImmutableArray<string>? signingFilter = null )
    {
        if ( !TryGetCredential( context ) )
        {
            return false;
        }

        if ( !Directory.Exists( directory ) )
        {
            context.Console.WriteError( $"The directory to sign does not exist: '{directory}'." );

            return false;
        }

        var effectiveSigningFilter = signingFilter ?? context.Product.SigningFilter;
        var signingFilterFile = TryWriteSigningFilterFile( context, effectiveSigningFilter );

        try
        {
            var filterArgument = signingFilterFile == null ? "" : $" --filelist \"{signingFilterFile}\"";

            foreach ( var filter in filters )
            {
                var matches = Directory.EnumerateFiles( directory, filter ).ToList();

                if ( matches.Count == 0 )
                {
                    continue;
                }

                var alreadySigned = matches.Where( IsSignedPackage ).ToList();

                if ( alreadySigned.Count == 0 )
                {
                    // Nothing to skip, so the pattern goes to the tool as it is and the whole set is one call.
                    if ( !DotNetTool.SignClient.Invoke(
                            context,
                            $"Sign --baseDirectory \"{directory}\" --input {filter}{filterArgument}" ) )
                    {
                        return false;
                    }

                    continue;
                }

                foreach ( var file in alreadySigned )
                {
                    context.Console.WriteMessage( $"Already signed, skipping: {Path.GetFileName( file )}" );
                }

                // The remainder is named one by one, because the tool selects by pattern and there is no
                // pattern that means "these and not those".
                foreach ( var file in matches.Except( alreadySigned, StringComparer.OrdinalIgnoreCase ) )
                {
                    if ( !DotNetTool.SignClient.Invoke(
                            context,
                            $"Sign --baseDirectory \"{directory}\" --input {Path.GetFileName( file )}{filterArgument}" ) )
                    {
                        return false;
                    }
                }
            }
        }
        finally
        {
            if ( signingFilterFile != null )
            {
                File.Delete( signingFilterFile );
            }
        }

        if ( verifyNuGetPackages && !TryVerifyNuGetPackages( context, directory ) )
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether a package already carries its own signature, in which case signing it again is not a
    /// no-op: the service rejects it, and the caller sees a 500 with nothing to say which file caused it.
    /// </summary>
    /// <remarks>
    /// A product can pack more than once into one directory -- a build that produces both a public and a
    /// commit-stamped variant does exactly that -- and the second pass would otherwise be handed the packages the
    /// first one signed. Skipping them makes signing a directory idempotent, which is what a caller naming a
    /// directory rather than a file expects.
    /// </remarks>
    private static bool IsSignedPackage( string file )
    {
        var extension = Path.GetExtension( file );

        if ( !extension.Equals( ".nupkg", StringComparison.OrdinalIgnoreCase )
             && !extension.Equals( ".snupkg", StringComparison.OrdinalIgnoreCase ) )
        {
            return false;
        }

        try
        {
            using var archive = ZipFile.OpenRead( file );

            return archive.GetEntry( ".signature.p7s" ) != null;
        }
        catch ( InvalidDataException )
        {
            // Not a readable archive. Let the sign service be the one to complain about it.
            return false;
        }
    }

    /// <summary>
    /// Writes the signing filter to a file for SignClient's <c>--filelist</c>, or returns <c>null</c> when the
    /// product declares none, in which case the service signs everything inside a container.
    /// </summary>
    private static string? TryWriteSigningFilterFile( BuildContext context, ImmutableArray<string> signingFilter )
    {
        if ( signingFilter.IsDefaultOrEmpty )
        {
            context.Console.WriteWarning(
                "No signing filter is declared, so every file inside a signed container will be signed, "
                + "including third-party dependencies. Set Product.SigningFilter." );

            return null;
        }

        var path = Path.Combine( Path.GetTempPath(), "sign-filter-" + Guid.NewGuid().ToString( "N" ) + ".txt" );

        // The service splits on '\n' and treats a leading '!' as an exclusion.
        File.WriteAllText( path, string.Join( "\n", signingFilter ) );

        return path;
    }

    /// <summary>
    /// The formats whose signature belongs to the container rather than to the bytes inside it, and which therefore
    /// have to be handed to the sign service one by one.
    /// </summary>
    /// <remarks>
    /// The sign service treats these as archives: it descends into one, signs what it finds, and saves it again. So
    /// putting one inside another archive is not merely wasteful, it changes the outcome -- the contents come back
    /// signed and the package itself does not carry the signature its format defines, which nothing notices until a
    /// consumer verifies it.
    /// </remarks>
    private static readonly ImmutableHashSet<string> _containerFormats =
        ImmutableHashSet.Create( StringComparer.OrdinalIgnoreCase, ".nupkg", ".snupkg", ".vsix" );

    /// <summary>
    /// Signs, in place, an explicit set of paths of any kind, choosing for each how it has to be presented to the
    /// sign service.
    /// </summary>
    /// <remarks>
    /// This is the distinction a caller should not have to make. A package must be handed over on its own, because
    /// wrapping it would sign its contents and leave the package unsigned; assemblies and executables can be signed
    /// either way, so they are batched into one archive to save a round trip each.
    /// </remarks>
    public static bool TrySignPaths( BuildContext context, IReadOnlyCollection<string> paths, bool verifyNuGetPackages = true )
    {
        var containers = paths
            .Where( p => _containerFormats.Contains( Path.GetExtension( p ) ) )
            .ToList();

        var others = paths.Except( containers, StringComparer.OrdinalIgnoreCase ).ToList();

        // Grouped by directory, because the sign service takes a base directory and a pattern rather than a path.
        foreach ( var group in containers.GroupBy( Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase ) )
        {
            var directory = group.Key;

            if ( string.IsNullOrEmpty( directory ) )
            {
                context.Console.WriteError( $"The path to sign is not absolute: '{group.First()}'." );

                return false;
            }

            foreach ( var file in group )
            {
                if ( !TrySign( context, directory, [Path.GetFileName( file )], verifyNuGetPackages: false ) )
                {
                    return false;
                }
            }

            if ( verifyNuGetPackages
                 && group.Any( f => Path.GetExtension( f ).Equals( ".nupkg", StringComparison.OrdinalIgnoreCase ) )
                 && !TryVerifyNuGetPackages( context, directory ) )
            {
                return false;
            }
        }

        if ( others.Count > 0 && !TrySignFiles( context, others ) )
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Signs, in place, an explicit list of files, which is how assemblies and executables are signed.
    /// </summary>
    /// <remarks>
    /// The sign service signs an assembly or an executable handed to it directly -- both are among the extensions it
    /// supports -- so the archive here buys throughput, not capability. Each call is a round trip to a remote
    /// service and a product signs hundreds of assemblies, so they are packed into one archive, which the service
    /// descends into and signs entry by entry, and the signed entries are written back over the originals.
    /// </remarks>
    public static bool TrySignFiles( BuildContext context, IReadOnlyCollection<string> files )
    {
        if ( files.Count == 0 )
        {
            context.Console.WriteWarning( "No file to sign." );

            return true;
        }

        if ( !TryGetCredential( context ) )
        {
            return false;
        }

        var missingFiles = files.Where( f => !File.Exists( f ) ).ToList();

        if ( missingFiles.Count > 0 )
        {
            foreach ( var missingFile in missingFiles )
            {
                context.Console.WriteError( $"File to sign not found: '{missingFile}'." );
            }

            return false;
        }

        var workDirectory = Path.Combine( Path.GetTempPath(), "sign-" + Guid.NewGuid().ToString( "N" ) );

        try
        {
            Directory.CreateDirectory( workDirectory );

            var archivePath = Path.Combine( workDirectory, "files-to-sign.zip" );

            // The entry names are ordinals rather than the file names, because two files being signed can share a
            // name -- the same assembly for several target frameworks -- and because a path assembled from the
            // originals can exceed the maximum length once it is under a temporary directory.
            var entryNames = new Dictionary<string, string>( StringComparer.Ordinal );

            using ( var archive = ZipFile.Open( archivePath, ZipArchiveMode.Create ) )
            {
                var ordinal = 0;

                foreach ( var file in files )
                {
                    var entryName = ordinal.ToString( CultureInfo.InvariantCulture )
                                    + Path.GetExtension( file );

                    entryNames.Add( entryName, file );
                    archive.CreateEntryFromFile( file, entryName, CompressionLevel.Fastest );
                    ordinal++;
                }
            }

            context.Console.WriteMessage( $"Signing {files.Count} file(s) through '{Path.GetFileName( archivePath )}'." );

            // No --filelist here, deliberately. The filter exists to keep the service from signing files a
            // container happens to carry; the caller of this method named every file itself. Worse, the entries
            // are ordinals, so a filter written for real names would match none of them and the call would
            // silently sign nothing.
            if ( !DotNetTool.SignClient.Invoke(
                    context,
                    $"Sign --baseDirectory \"{workDirectory}\" --input {Path.GetFileName( archivePath )}" ) )
            {
                return false;
            }

            using ( var archive = ZipFile.OpenRead( archivePath ) )
            {
                foreach ( var entry in archive.Entries )
                {
                    if ( !entryNames.TryGetValue( entry.FullName, out var targetFile ) )
                    {
                        // The sign service returns the archive it was given, so an unknown entry means it rewrote
                        // the layout. Signing the wrong file back over a product binary is worse than failing.
                        context.Console.WriteError( $"The signed archive contains an unexpected entry '{entry.FullName}'." );

                        return false;
                    }

                    entry.ExtractToFile( targetFile, true );
                    entryNames.Remove( entry.FullName );
                }
            }

            if ( entryNames.Count > 0 )
            {
                foreach ( var missing in entryNames )
                {
                    context.Console.WriteError( $"The signed archive does not contain '{missing.Value}'." );
                }

                return false;
            }

            return true;
        }
        finally
        {
            if ( Directory.Exists( workDirectory ) )
            {
                try
                {
                    Directory.Delete( workDirectory, true );
                }
                catch ( IOException )
                {
                    // Losing a temporary directory must not fail a build that has otherwise signed correctly.
                }
            }
        }
    }

    private static bool TryVerifyNuGetPackages( BuildContext context, string directory )
    {
        foreach ( var package in Directory.EnumerateFiles( directory, "*.nupkg" ) )
        {
            if ( !ToolInvocationHelper.InvokeTool(
                    context.Console,
                    "dotnet",
                    $"nuget verify --all \"{package}\"",
                    context.RepoDirectory ) )
            {
                context.Console.WriteError( $"Signature verification failed for '{Path.GetFileName( package )}'." );

                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks that the credential SignClient needs is present, and reports the missing one by name rather than
    /// letting the tool fail on an empty secret.
    /// </summary>
    /// <remarks>
    /// SignClient presents the build agent's own service principal rather than signing in as a named user, so the
    /// credential is the agent's. <c>SIGNSERVER_SECRET</c> held the former user account's password and is read by
    /// nothing.
    /// </remarks>
    private static bool TryGetCredential( BuildContext context )
    {
        var missing = new[] { EnvironmentVariableNames.AzureClientId, EnvironmentVariableNames.AzureTenantId, EnvironmentVariableNames.AzureClientSecret }
            .Where( name => string.IsNullOrEmpty( Environment.GetEnvironmentVariable( name ) ) )
            .ToList();

        if ( missing.Count > 0 )
        {
            context.Console.WriteError(
                $"Cannot sign because {string.Join( ", ", missing )} {(missing.Count == 1 ? "is" : "are")} not defined. "
                + "Signing uses the build agent's service principal." );

            return false;
        }

        return true;
    }
}