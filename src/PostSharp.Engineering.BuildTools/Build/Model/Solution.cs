// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build.Model
{
    /// <summary>
    /// Represents an individual Visual Studio solution, project or build script.
    /// </summary>
    [PublicAPI]
    public abstract class Solution
    {
        /// <summary>
        /// Gets the name of the solution.
        /// </summary>
        public virtual string Name => Path.GetFileName( this.SolutionPath );

        /// <summary>
        /// Gets the full path of the solution file.
        /// </summary>
        public string SolutionPath { get; }

        /// <summary>
        /// Gets or sets a value indicating whether the current solution should be built only during a <c>test</c> command.
        /// </summary>
        public bool IsTestOnly { get; init; }

        /// <summary>
        /// Gets or sets a value indicating whether a call to the <see cref="Pack"/> method should be explicitly preceded by
        /// a call to the <see cref="Build"/> method.
        /// </summary>
        public bool PackRequiresExplicitBuild { get; init; }

        /// <summary>
        /// Gets or sets a value indicating whether the current solution supports test coverage.
        /// </summary>
        public bool SupportsTestCoverage { get; init; }

        /// <summary>
        /// Gets or sets a value indicating whether the current solution is affected by the <c>codestyle format</c> command.
        /// </summary>
        public bool CanFormatCode { get; init; }

        /// <summary>
        /// Gets or sets the list of exclusions from the <c>codestyle format</c> command. It can contain globbing patterns like <c>**</c> and <c>*</c>.
        /// </summary>
        public string[] FormatExclusions { get; init; } = [];

        /// <summary>
        /// Gets or sets the method (<see cref="Build"/>, <see cref="Test"/> or <see cref="Pack"/>) that must be invoked when executing the <c>build</c> command.
        /// </summary>
        public BuildMethod? BuildMethod { get; init; }

        /// <summary>
        /// Gets or sets the method (typically <see cref="Test"/> or <c>None</c>) that must be invoked when executing the <c>test</c> command.
        /// </summary>
        public BuildMethod? TestMethod { get; init; }

        public string? SolutionFilterPathForInspectCode { get; init; }

        /// <summary>
        /// Gets or sets the environment variables that are passed to the build process.
        /// </summary>
        public ImmutableDictionary<string, string?> EnvironmentVariables { get; init; } = ImmutableDictionary<string, string?>.Empty;

        /// <summary>
        /// Gets the method (<see cref="Build"/>, <see cref="Test"/> or <see cref="Pack"/>) that must be invoked when executing the <c>build</c> command.
        /// This method returns <see cref="Pack"/> by default. If the <see cref="BuildMethod"/> property has been defined, it returns its value.
        /// If <see cref="IsTestOnly"/> is <c>true</c>, this method returns <see cref="Build"/>.
        /// </summary>
        public BuildMethod GetBuildMethod() => this.BuildMethod ?? (this.IsTestOnly ? Model.BuildMethod.Build : Model.BuildMethod.Pack);

        /// <summary>
        /// Builds the current solution, but does not pack it.
        /// </summary>
        public abstract bool Build( BuildContext context, BuildSettings settings );

        /// <summary>
        /// Packs the current solution. Unless <see cref="PackRequiresExplicitBuild"/> is defined, the implementation should build
        /// the solution as a part of packing the artifacts.
        /// </summary>
        public abstract bool Pack( BuildContext context, BuildSettings settings );

        /// <summary>
        /// Builds and tests the current solution.
        /// </summary>
        public abstract bool Test( BuildContext context, BuildSettings settings );

        /// <summary>
        /// Restores the packages and artifacts needed by the current solution.
        /// </summary>
        public abstract bool Restore( BuildContext context, BuildSettings settings );

        public virtual IEnumerable<Solution> GetFormattableSolutions( BuildContext context ) => [this];

        protected Solution( string solutionPath )
        {
            this.SolutionPath = solutionPath;
        }

        /// <summary>
        /// Executes a specified <see cref="Model.BuildMethod"/>.
        /// </summary>
        public bool Execute( BuildContext context, BuildSettings settings, BuildMethod buildMethod )
        {
            switch ( buildMethod )
            {
                case Model.BuildMethod.None:
                    return true;

                case Model.BuildMethod.Build:
                    return this.Build( context, settings );

                case Model.BuildMethod.Pack:
                    if ( this.PackRequiresExplicitBuild && !settings.NoDependencies )
                    {
                        if ( !this.Build( context, settings ) )
                        {
                            return false;
                        }
                    }

                    return this.Pack( context, settings );

                case Model.BuildMethod.Test:
                    return this.Test( context, settings );

                default:
                    throw new ArgumentOutOfRangeException( nameof(buildMethod) );
            }
        }
    }
}