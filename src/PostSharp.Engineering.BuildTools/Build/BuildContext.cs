// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;
using Spectre.Console.Cli;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Build
{
    /// <summary>
    /// Exposes all the current build context, i.e. anything that depends on the environment or the current product, but not depending on
    /// the command line.
    /// </summary>
    public class BuildContext
    {
        /// <summary>
        /// Gets an object that allows to write messages to the console.
        /// </summary>
        public ConsoleHelper Console { get; }

        /// <summary>
        /// Gets the root directory of the git repository.
        /// </summary>
        public string RepoDirectory { get; }

        public BaseCommandData CommandData { get; }

        /// <summary>
        /// Gets the current <see cref="Model.Product"/> definition.
        /// </summary>
        public Product Product => this.CommandData.Product;

        /// <summary>
        /// Gets the name of the current git branch. Can be for instance a topic branch.
        /// </summary>
        public string Branch { get; }

        public CommandContext CommandContext { get; }

        public bool UseProjectDirectoryAsWorkingDirectory { get; }

        public string GetWorkingDirectory( string projectOrSolution )
            => this.UseProjectDirectoryAsWorkingDirectory
                ? Path.GetDirectoryName( projectOrSolution )!
                : Environment.CurrentDirectory;

        /// <summary>
        /// Gets a value indicating whether the current device is a guest device, as opposed to a device owned and configured by PostSharp.
        /// The main difference is that guest devices use feed packages while company devices use TeamCity artefacts.
        /// </summary>
        public static bool IsGuestDevice
            => !bool.TryParse( Environment.GetEnvironmentVariable( EnvironmentVariableNames.IsPostSharpOwned ), out var value ) || !value;

        /// <summary>
        /// Gets the full path of the current manifest file (i.e. the file called <c>My.Product.version.props</c>)
        /// for a given <see cref="BuildConfiguration"/>.
        /// </summary>
        public string GetManifestFilePath( BuildConfiguration configuration )
            => Path.Combine(
                this.Product.GetPrivateArtifactsAbsoluteDirectory( this, configuration ),
                $"{this.Product.ProductName}.version.props" );

        private BuildContext(
            ConsoleHelper console,
            string repoDirectory,
            BaseCommandData commandData,
            string branch,
            CommandContext commandContext,
            bool useProjectDirectoryAsWorkingDirectory,
            CommonCommandSettings settings )
        {
            this.Console = console;
            this.RepoDirectory = repoDirectory;
            this.CommandData = commandData;
            this.Branch = branch;
            this.CommandContext = commandContext;
            this.UseProjectDirectoryAsWorkingDirectory = useProjectDirectoryAsWorkingDirectory;
            this.Settings = settings;
        }

        /// <summary>
        /// Tries to create a <see cref="BuildContext"/> from a <see cref="Spectre.Console.Cli.CommandContext"/>.
        /// </summary>
        public static bool TryCreate(
            CommandContext commandContext,
            CommonCommandSettings settings,
            [NotNullWhen( true )] out BuildContext? buildContext )
        {
            buildContext = null;
            var repoDirectory = FindRepoDirectory( Environment.CurrentDirectory );
            var console = new ConsoleHelper();

            if ( repoDirectory == null )
            {
                console.WriteError( "This tool must be called from a git repository." );

                return false;
            }

            if ( !GitHelper.TryGetCurrentBranch( console, repoDirectory, out var currentBranch ) )
            {
                return false;
            }

            buildContext = new BuildContext(
                console,
                repoDirectory,
                (BaseCommandData) commandContext.Data!,
                currentBranch,
                commandContext,
                useProjectDirectoryAsWorkingDirectory: false,
                settings );

            return true;
        }

        private static string? FindRepoDirectory( string directory )
        {
            if ( Directory.Exists( Path.Combine( directory, ".git" ) ) )
            {
                var gitIgnorePath = Path.Combine( directory, ".gitignore" );

                if ( !File.Exists( gitIgnorePath ) )
                {
                    throw new FileNotFoundException( $"The file '{gitIgnorePath}' must exist, even empty." );
                }

                // Resolve links and junctions.
                var realPath = FileSystemHelper.GetFinalPath( gitIgnorePath );

                return Path.GetDirectoryName( realPath );
            }
            else
            {
                var parentDirectory = Path.GetDirectoryName( directory );

                if ( parentDirectory != null )
                {
                    return FindRepoDirectory( parentDirectory );
                }
                else
                {
                    return null;
                }
            }
        }

        public BuildContext WithConsoleHelper( ConsoleHelper consoleHelper )
            => new(
                consoleHelper,
                this.RepoDirectory,
                this.CommandData,
                this.Branch,
                this.CommandContext,
                this.UseProjectDirectoryAsWorkingDirectory,
                this.Settings );

        public BuildContext WithUseProjectDirectoryAsWorkingDirectory( bool useProjectDirectoryAsWorkingDirectory )
            => new(
                this.Console,
                this.RepoDirectory,
                this.CommandData,
                this.Branch,
                this.CommandContext,
                useProjectDirectoryAsWorkingDirectory,
                this.Settings );

        [PublicAPI]
#pragma warning disable CA1822
        public bool IsRunningUnderContainer => DockerHelper.IsDockerBuild();
#pragma warning restore CA1822

        public bool IsContinuousIntegrationBuild => TeamCityHelper.IsTeamCityBuild( this.Settings );

        [PublicAPI]
        public CommonCommandSettings Settings { get; }

        internal TimeSpan BuildTimeout
        {
            get
            {
                var setting = this.Settings.Timeout;

#pragma warning disable CS0618 // Type or member is obsolete
                return setting == null ? this.Product.BuildTimeout : TimeSpan.FromMinutes( setting.Value );
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }

        // Adds a margin to dump the hanging processes.
        internal TimeSpan BuildTimeoutWithMargin => this.BuildTimeout.Add( TimeSpan.FromMinutes( 5 ) );
    }
}