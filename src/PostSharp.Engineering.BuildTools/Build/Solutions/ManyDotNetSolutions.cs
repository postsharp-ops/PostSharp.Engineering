// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

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

public class ManyDotNetSolutions : Solution
{
    private ImmutableArray<DotNetSolution> _solutions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManyDotNetSolutions"/> class.
    /// </summary>
    /// <param name="directory">A directory.</param>
    public ManyDotNetSolutions( string directory ) : base( directory )
    {
        // Default settings.
        this.IsTestOnly = true;
        this.BuildMethod = Model.BuildMethod.Build;
    }

    public override IEnumerable<Solution> GetFormattableSolutions( BuildContext context )
    {
        if ( !this.TryGetSolutions( context, out var solutions ) )
        {
            return [];
        }
        else
        {
            return solutions;
        }
    }

    private bool BuildOrTest( BuildContext context, BuildSettings settings, bool test, string verb )
    {
        var failedProjects = new List<DotNetSolution>();

        if ( !this.TryGetSolutions( context, out var solutions ) )
        {
            return false;
        }

        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim( Environment.ProcessorCount );
        var consoleSync = new object();

        foreach ( var solution in solutions )
        {
            // We need to build explicitly because some projects may not have a test target,
            // and may be ignored if we only test.

            var task = Task.Run( async () =>
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
                                failedProjects.Add( solution );
                            }
                        }
                    }
                    else
                    {
                        failedProjects.Add( solution );
                    }
                }
                catch ( TaskCanceledException )
                {
                    bufferingConsole.WriteError( "The build has been canceled." );
                    failedProjects.Add( solution );
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

    public override bool Build( BuildContext context, BuildSettings settings ) => this.BuildOrTest( context, settings, false, "Building" );

    public override bool Pack( BuildContext context, BuildSettings settings )
    {
        throw new NotSupportedException();
    }

    public override bool Test( BuildContext context, BuildSettings settings ) => this.BuildOrTest( context, settings, true, "Testing" );

    public override bool Restore( BuildContext context, BuildSettings settings )
    {
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

    private bool TryGetSolutions( BuildContext context, out ImmutableArray<DotNetSolution> solutions )
    {
        var rootDirectory = Path.Combine( context.RepoDirectory, this.SolutionPath );

        if ( !Directory.Exists( rootDirectory ) )
        {
            throw new FileNotFoundException( $"'{rootDirectory}' is not a valid directory." );
        }

        if ( this._solutions.IsDefault )
        {
            var builder = ImmutableArray.CreateBuilder<DotNetSolution>();

            bool AddFiles( string directory, string searchPattern, BuildMethod testMethod = Model.BuildMethod.Test )
            {
                var projFiles = Directory.GetFiles( directory, searchPattern );

                if ( projFiles.Length > 0 )
                {
                    builder.AddRange(
                        projFiles.Select( f => new DotNetSolution( f ) { EnvironmentVariables = this.EnvironmentVariables, TestMethod = testMethod } ) );

                    return true;
                }

                return false;
            }

            void ProcessDirectory( string directory )
            {
                // Do not process recursively if we find a file we can build.
                // The order of processing is significant.
                if ( AddFiles( directory, "*.proj", Model.BuildMethod.Build ) || AddFiles( directory, "*.sln" ) || AddFiles( directory, "*.csproj" ) )
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