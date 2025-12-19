// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Files;
using PostSharp.Engineering.BuildTools.Build.Model;
using System;
using System.Diagnostics.CodeAnalysis;

namespace PostSharp.Engineering.BuildTools.Build.Bumping;

internal class DefaultBumpStrategy : IBumpStrategy
{
    public bool TryBumpVersion(
        Product product,
        BuildContext context,
        [NotNullWhen( true )] out Version? oldVersion,
        [NotNullWhen( true )] out Version? newVersion )
    {
        if ( !MainVersionFile.TryRead( context, out var currentMainVersionFile, out _ ) )
        {
            oldVersion = null;
            newVersion = null;

            return false;
        }

        oldVersion = new Version( currentMainVersionFile.MainVersion );

        // Increment the version.
        newVersion = new Version(
            oldVersion.Major,
            oldVersion.Minor,
            oldVersion.Build + 1 );

        // Save the MainVersion.props with new version.
        if ( !currentMainVersionFile.TryWrite( context, newVersion, null, out _ ) )
        {
            return false;
        }

        context.Console.WriteSuccess( $"Bumping the '{context.Product.ProductName}' version from '{oldVersion}' to '{newVersion}' was successful." );

        return true;
    }
}