// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build;

/// <summary>
/// Selects the solutions that the <c>build</c> and <c>test</c> commands process, from <see cref="BuildSettings.SolutionId"/>
/// and <see cref="BuildSettings.LastSolution"/>.
/// </summary>
internal static class SolutionSelection
{
    public static bool TryGetSolutions( BuildContext context, BuildSettings settings, [NotNullWhen( true )] out Solution[]? solutions )
    {
        var allSolutions = context.Product.Solutions;

        if ( settings.SolutionId != null && settings.LastSolution != null )
        {
            context.Console.WriteError( "The --solution and --to options cannot be used together." );
            solutions = null;

            return false;
        }

        if ( settings.SolutionId != null )
        {
            var index = FindSolution( allSolutions, settings.SolutionId );

            if ( index < 0 )
            {
                WriteUnknownSolutionError( context, settings.SolutionId, "--solution" );
                solutions = null;

                return false;
            }

            solutions = [allSolutions[index]];

            return true;
        }

        if ( settings.LastSolution != null )
        {
            var lastIndex = FindSolution( allSolutions, settings.LastSolution );

            if ( lastIndex < 0 )
            {
                WriteUnknownSolutionError( context, settings.LastSolution, "--to" );
                solutions = null;

                return false;
            }

            solutions = allSolutions[..(lastIndex + 1)];

            return true;
        }

        solutions = allSolutions;

        return true;
    }

    // The id is the 1-based position that the list-solutions command shows. The alias (Solution.Alias) and the file name of
    // the solution, with or without its extension, do not change when a solution is added before the designated one.
    private static int FindSolution( Solution[] solutions, string value )
    {
        if ( int.TryParse( value, NumberStyles.None, CultureInfo.InvariantCulture, out var id ) )
        {
            return id >= 1 && id <= solutions.Length ? id - 1 : -1;
        }

        var aliasIndex = Array.FindIndex( solutions, s => string.Equals( s.Alias, value, StringComparison.OrdinalIgnoreCase ) );

        if ( aliasIndex >= 0 )
        {
            return aliasIndex;
        }

        return Array.FindIndex(
            solutions,
            s => string.Equals( s.Name, value, StringComparison.OrdinalIgnoreCase )
                 || string.Equals( Path.GetFileNameWithoutExtension( s.SolutionPath ), value, StringComparison.OrdinalIgnoreCase ) );
    }

    private static void WriteUnknownSolutionError( BuildContext context, string value, string option )
    {
        var validValues = context.Product.Solutions.Select(
            ( s, i ) => $"{(i + 1).ToString( CultureInfo.InvariantCulture )} ({s.Alias ?? Path.GetFileNameWithoutExtension( s.SolutionPath )})" );

        context.Console.WriteError( $"'{value}' is not a solution of the product. The value of {option} is one of: {string.Join( ", ", validValues )}." );
    }
}
