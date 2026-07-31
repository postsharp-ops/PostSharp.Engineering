// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Build.Solutions;

/// <summary>
/// A <see cref="Solution"/> that discovers every buildable project under a directory and builds each of them
/// independently, as a distinct scenario. Derived classes only decide which build engine each scenario is built with;
/// discovery, scheduling, restore and reporting are implemented here.
/// </summary>
[PublicAPI]
public abstract class ManySolutions : Solution
{
    private ImmutableArray<TestableSolution> _solutions;

    /// <param name="directory">A directory, relative to the root of the repository.</param>
    protected ManySolutions( string directory ) : base( directory )
    {
        // Default settings.
        this.IsTestOnly = true;
        this.BuildMethod = Model.BuildMethod.Build;
    }

    /// <summary>
    /// Creates the <see cref="TestableSolution"/> that builds a single discovered scenario. This is the only
    /// difference between the implementations of <see cref="ManySolutions"/>.
    /// </summary>
    protected abstract TestableSolution CreateSolution( string projectPath, BuildMethod testMethod );

    /// <summary>
    /// Determines whether the scenarios can be built on the current machine. When this method returns <c>false</c>,
    /// the scenarios are skipped and the build is reported as successful. The implementation must log the reason.
    /// </summary>
    protected virtual bool IsSupportedOnThisPlatform( BuildContext context ) => true;

    public override IEnumerable<Solution> GetFormattableSolutions( BuildContext context )
        => this.TryGetSolutions( context, out var solutions ) ? solutions : [];

    public override bool Build( BuildContext context, BuildSettings settings ) => this.BuildOrTest( context, settings, false, "Building" );

    public override bool Test( BuildContext context, BuildSettings settings ) => this.BuildOrTest( context, settings, true, "Testing" );

    public override bool Pack( BuildContext context, BuildSettings settings ) => throw new NotSupportedException();

    public override bool Restore( BuildContext context, BuildSettings settings )
    {
        if ( !this.IsSupportedOnThisPlatform( context ) )
        {
            return true;
        }

        if ( !this.TryGetSolutions( context, out var solutions ) )
        {
            return false;
        }

        var failures = 0;

        foreach ( var solution in solutions )
        {
            if ( !solution.Restore( context, settings ) )
            {
                failures++;
            }
        }

        if ( failures > 0 )
        {
            context.Console.WriteError( $"{failures} project(s) failed to restore." );

            return false;
        }

        return true;
    }

    private bool BuildOrTest( BuildContext context, BuildSettings settings, bool test, string verb )
    {
        if ( !this.IsSupportedOnThisPlatform( context ) )
        {
            return true;
        }

        if ( !this.TryGetSolutions( context, out var solutions ) )
        {
            return false;
        }

        var failedProjects = new List<Solution>();
        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim( Environment.ProcessorCount );
        var consoleSync = new object();

        foreach ( var solution in solutions )
        {
            // We need to build explicitly because some projects may not have a test target,
            // and may be ignored if we only test.

            var task = Task.Run(
                async () =>
                {
                    await semaphore.WaitAsync();

                    // Write the build output to a buffer so we don't get mixed output.
                    var bufferingConsole = BufferingConsoleHelper.Create( context.Console );
                    var localContext = context.WithConsoleHelper( bufferingConsole ).WithUseProjectDirectoryAsWorkingDirectory( true );

                    try
                    {
                        if ( solution.Build( localContext, settings ) )
                        {
                            if ( test && solution.TestMethod == Model.BuildMethod.Test )
                            {
                                if ( !solution.Test( localContext, settings ) )
                                {
                                    lock ( failedProjects )
                                    {
                                        failedProjects.Add( solution );
                                    }
                                }
                            }
                        }
                        else
                        {
                            lock ( failedProjects )
                            {
                                failedProjects.Add( solution );
                            }
                        }
                    }
                    catch ( TaskCanceledException )
                    {
                        bufferingConsole.WriteError( "The build has been canceled." );

                        lock ( failedProjects )
                        {
                            failedProjects.Add( solution );
                        }
                    }
                    finally
                    {
                        semaphore.Release();

                        // Write the output, but within a lock to avoid mixes.
                        lock ( consoleSync )
                        {
                            context.Console.WriteHeading( $"{verb} {solution.SolutionPath}" );
                            bufferingConsole.Replay();
                        }
                    }
                } );

            tasks.Add( task );
        }

        Task.WaitAll( tasks.ToArray() );

        if ( failedProjects.Count > 0 )
        {
            context.Console.WriteError( $"{failedProjects.Count} project(s) failed: {string.Join( ", ", failedProjects.Select( x => x.SolutionPath ) )}." );

            return false;
        }

        return true;
    }

    private bool TryGetSolutions( BuildContext context, out ImmutableArray<TestableSolution> solutions )
    {
        var rootDirectory = Path.Combine( context.RepoDirectory, this.SolutionPath );

        if ( !Directory.Exists( rootDirectory ) )
        {
            throw new FileNotFoundException( $"'{rootDirectory}' is not a valid directory." );
        }

        if ( this._solutions.IsDefault )
        {
            var builder = ImmutableArray.CreateBuilder<TestableSolution>();

            bool AddFiles( string directory, BuildMethod testMethod, params string[] searchPatterns )
            {
                // Distinct because a single file can match more than one pattern: on a volume that keeps 8.3 short
                // names, Directory.GetFiles with a three-character pattern extension such as "*.sln" also returns
                // "*.slnx" files, which would otherwise be added twice.
                var projFiles = searchPatterns
                    .SelectMany( p => Directory.GetFiles( directory, p ) )
                    .Distinct( StringComparer.OrdinalIgnoreCase )
                    .ToArray();

                if ( projFiles.Length > 0 )
                {
                    builder.AddRange( projFiles.Select( f => this.CreateSolution( f, testMethod ) ) );

                    return true;
                }

                return false;
            }

            void ProcessDirectory( string directory )
            {
                // Do not process recursively if we find a file we can build.
                // The order of processing is significant. Both solution formats are treated together: .slnx is the
                // newer XML solution file, and a directory may hold either.
                if ( AddFiles( directory, Model.BuildMethod.Build, "*.proj" )
                     || AddFiles( directory, Model.BuildMethod.Test, "*.sln", "*.slnx" )
                     || AddFiles( directory, Model.BuildMethod.Test, "*.csproj" )
                     || AddFiles( directory, Model.BuildMethod.Test, "Program.cs" ) )
                {
                    return;
                }

                // Continue recursively if we have not found anything.
                foreach ( var subdirectory in Directory.GetDirectories( directory ) )
                {
                    ProcessDirectory( subdirectory );
                }
            }

            ProcessDirectory( rootDirectory );

            this._solutions = builder.ToImmutable();
        }

        solutions = this._solutions;

        return true;
    }
}
