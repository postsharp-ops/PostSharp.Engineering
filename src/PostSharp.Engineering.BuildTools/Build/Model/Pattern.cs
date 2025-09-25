// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.Build.Model
{
    /// <summary>
    /// A file globbing pattern, i.e. something like <c>**\*.cs</c>. This class is immutable. 
    /// </summary>
    [PublicAPI]
    public sealed class Pattern
    {
        private ImmutableArray<(ParametricString Pattern, bool IsExclude)> Items { get; }

        private Pattern( ImmutableArray<(ParametricString Pattern, bool IsExclude)> items )
        {
            this.Items = items;
        }

        public bool IsEmpty => this.Items.IsDefaultOrEmpty;

        /// <summary>
        /// Gets an empty <see cref="Pattern"/>.
        /// </summary>
        public static Pattern Empty { get; } = new( ImmutableArray<(ParametricString Pattern, bool IsExclude)>.Empty );

        /// <summary>
        /// Adds new including patterns to the current pattern and returns the result.
        /// </summary>
        public Pattern Add( params ParametricString[] patterns ) => new( this.Items.AddRange( patterns.Select( p => (p, false) ) ) );

        /// <summary>
        /// Adds new excluding patterns to the current pattern and returns the result.
        /// </summary>
        public Pattern Remove( params ParametricString[] patterns ) => new( this.Items.AddRange( patterns.Select( p => (p, true) ) ) );

        /// <summary>
        /// Appends a pattern after the current pattern and returns the result.
        /// </summary>
        public Pattern Append( Pattern pattern ) => new( this.Items.AddRange( pattern.Items ) );

        /// <summary>
        /// Creates a new additive pattern.
        /// </summary>
        /// <param name="patterns"></param>
        /// <returns></returns>
        public static Pattern Create( params ParametricString[] patterns ) => Empty.Add( patterns );

        /// <summary>
        /// Verifies that every item of the current pattern matches some files on the file system,
        /// and writes an error to the console when one does not.
        /// </summary>
        public bool Verify( BuildContext context, string directory, BuildArguments buildArguments )
        {
            var success = true;
            var absoluteDirectory = Path.Combine( context.RepoDirectory, directory );

            foreach ( var pattern in this.Items )
            {
                if ( pattern.IsExclude )
                {
                    continue;
                }

                var matcher = new Matcher( StringComparison.OrdinalIgnoreCase );
                var expandedPattern = pattern.Pattern.ToString( buildArguments );
                matcher.AddInclude( expandedPattern );

                var matchingResult =
                    matcher.Execute( new DirectoryInfoWrapper( new DirectoryInfo( absoluteDirectory ) ) );

                if ( !matchingResult.HasMatches )
                {
                    context.Console.WriteError( $"The pattern '{absoluteDirectory}\\{expandedPattern}' does not match any file." );
                    success = false;
                }
            }

            return success;
        }

        /// <summary>
        /// Gets the files in a given directory matching the current pattern.
        /// </summary>
        public bool TryGetFiles( string directory, BuildArguments buildArguments, List<FilePatternMatch> files )
        {
            var matcher = new Matcher( StringComparison.OrdinalIgnoreCase );

            foreach ( var pattern in this.Items )
            {
                var file = pattern.Pattern.ToString( buildArguments );

                if ( pattern.IsExclude )
                {
                    matcher.AddExclude( file );
                }
                else
                {
                    matcher.AddInclude( file );
                }
            }

            var matchingResult =
                matcher.Execute( new DirectoryInfoWrapper( new DirectoryInfo( directory ) ) );

            if ( !matchingResult.HasMatches )
            {
                return false;
            }
            else
            {
                files.AddRange( matchingResult.Files );
            }

            return true;
        }

        public override string ToString() => string.Join( " ", this.Items.Select( i => (i.IsExclude ? "-" : "+") + i.Pattern ) );
    }
}