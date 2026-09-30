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
        /// Gets a value indicating whether the test projects of the solution are Microsoft.Testing.Platform test applications
        /// that run on the test agents of the product: <c>Build.ps1 build</c> packs them into test archives, and
        /// <c>generate-scripts</c> reads them to plan the build configurations of <see cref="Product.TestAgents"/>. Only these solutions are read, because evaluating the projects of every
        /// solution of a repository would cost time for nothing. See <c>doc/testing-platform.md</c>.
        /// </summary>
        public bool ContainsTestApplications { get; init; }

        /// <summary>
        /// Gets the test platform that runs the tests of the solution, when it differs from <see cref="Product.TestRunner"/>.
        /// The default value, <c>null</c>, means that the solution uses the test platform of the product.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This lets a product whose tests use VSTest contain a solution of Microsoft.Testing.Platform test applications,
        /// for instance a solution of platform tests that the test agents also run. The .NET SDK 10 refuses to run such an
        /// application in the mode of VSTest, so <c>Build.ps1 test</c> runs <c>dotnet test</c> for this solution in the mode
        /// of Microsoft.Testing.Platform, from a directory whose <c>global.json</c> selects that mode. See
        /// <c>doc/testing-platform.md</c>.
        /// </para>
        /// <para>
        /// The reverse is not supported: when <see cref="Product.TestRunner"/> is
        /// <see cref="Model.TestRunner.MicrosoftTestingPlatform"/>, the <c>global.json</c> of the repository selects that mode
        /// for every <c>dotnet test</c> of the repository, so a solution cannot use <see cref="Model.TestRunner.VSTest"/>.
        /// </para>
        /// </remarks>
        public TestRunner? TestRunner { get; init; }

        /// <summary>
        /// Gets the test platform that runs the tests of the solution: <see cref="TestRunner"/>, or else the one of the product.
        /// </summary>
        internal TestRunner GetTestRunner( Product product ) => this.TestRunner ?? product.TestRunner;

        /// <summary>
        /// Gets the test platform that runs the tests of the solution, and reports an error when the solution sets a test
        /// platform that the product does not support. See <see cref="TestRunner"/>.
        /// </summary>
        internal bool TryGetTestRunner( BuildContext context, out TestRunner testRunner )
        {
            testRunner = this.GetTestRunner( context.Product );

            if ( testRunner == Model.TestRunner.VSTest && context.Product.TestRunner == Model.TestRunner.MicrosoftTestingPlatform )
            {
                context.Console.WriteError(
                    $"The solution '{this.Name}' sets TestRunner to VSTest, but the product uses Microsoft.Testing.Platform, whose "
                    + "global.json selects that mode for every 'dotnet test' of the repository." );

                return false;
            }

            return true;
        }

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