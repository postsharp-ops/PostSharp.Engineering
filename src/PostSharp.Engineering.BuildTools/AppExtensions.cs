// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.BillOfMaterials;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Bumping;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Build.Publishing;
using PostSharp.Engineering.BuildTools.Build.Swapping;
using PostSharp.Engineering.BuildTools.Build.Testing;
using PostSharp.Engineering.BuildTools.CodeStyle;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Dependencies;
using PostSharp.Engineering.BuildTools.DotNetTools;
using PostSharp.Engineering.BuildTools.Tools;
using PostSharp.Engineering.BuildTools.Tools.Csproj;
using PostSharp.Engineering.BuildTools.Tools.Git;
using PostSharp.Engineering.BuildTools.Tools.NuGet;
using PostSharp.Engineering.BuildTools.Tools.Processes;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Tools.XmlDoc;
using Spectre.Console.Cli;
using System;
using System.Linq;

namespace PostSharp.Engineering.BuildTools
{
    [PublicAPI]
    public static class AppExtensions
    {
        /// <summary>
        /// Adds <see cref="Product"/>-related commands to a <see cref="CommandApp"/>.
        /// </summary>
        [Obsolete( "Use EngineeringApp instead." )]
        public static void AddProductCommands( this CommandApp app, Product product )
        {
            AddCommands( app, product );
        }

        internal static void AddCommands( this CommandApp app, Product product )
        {
            var data = new BaseCommandData( product );

            app.Configure( root =>
            {
                root.Settings.StrictParsing = true;

                if ( product.AddDefaultCommands )
                {
                    root.AddCommand<PrepareCommand>( "prepare" )
                        .WithData( data )
                        .WithDescription( "Creates the files that are required to build the product" );

                    root.AddCommand<BuildCommand>( "build" )
                        .WithData( data )
                        .WithDescription( "Builds all packages in the product (implies 'prepare')" );

                    root.AddCommand<GenerateScriptsCommand>( "generate-scripts" )
                        .WithData( data )
                        .WithDescription( "Generates the CI and Docker scripts" );

                    root.AddCommand<ListSolutionsCommand>( "list-solutions" )
                        .WithData( data )
                        .WithDescription( "Lists the solutions in the build sequence." );

                    root.AddCommand<TestCommand>( "test" )
                        .WithData( data )
                        .WithDescription( "Builds all packages then run all tests (implies 'build')" );

                    root.AddCommand<VerifyCommand>( "verify" )
                        .WithData( data )
                        .WithDescription( "Verify that the dependencies of public artifacts have already been publicly deployed" );

                    root.AddCommand<PrePublishCommand>( "prepublish" )
                        .WithData( data )
                        .WithDescription( "Prepares publishing of all packages that have been previously built by the 'build' command" );

                    root.AddCommand<PublishCommand>( "publish" )
                        .WithData( data )
                        .WithDescription( "Publishes all packages that have been previously built by the 'build' command" );

                    root.AddCommand<PostPublishCommand>( "postpublish" )
                        .WithData( data )
                        .WithDescription( "Finalizes publishing of all packages that have been previously built by the 'build' command" );

                    if ( product.Configurations.All.Any( c => c.Swappers is { Length: > 0 } ) )
                    {
                        root.AddCommand<SwapCommand>( "swap" )
                            .WithData( data )
                            .WithDescription( "Swaps deployment slots" );
                    }

                    // We add the bump command even for non-versioned products because it makes 
                    // orchestration of bumping all products easier.
                    root.AddCommand<BumpCommand>( "bump" )
                        .WithData( data )
                        .WithDescription( "Bumps the version of this product" );

                    root.AddCommand<GenerateThirdPartyNoticesCommand>( "third-party-notices" )
                        .WithData( data )
                        .WithDescription( "Generates THIRD-PARTY-NOTICES.md" );

                    root.AddBranch(
                        "dependencies",
                        dependencies =>
                        {
                            dependencies.AddCommand<ListDependenciesCommand>( "list" )
                                .WithData( data )
                                .WithDescription( "Lists the dependencies of this product" );

                            dependencies.AddCommand<SetDependenciesCommand>( "set" )
                                .WithData( data )
                                .WithDescription( "Sets how dependencies should be consumed." );

                            dependencies.AddCommand<ResetDependenciesCommand>( "reset" )
                                .WithData( data )
                                .WithDescription( "Resets any change done with the 'set' command and revert to the configuration as stored in source code." );

                            dependencies.AddCommand<PrintDependenciesCommand>( "print" )
                                .WithData( data )
                                .WithDescription( "Prints the dependency file." );

                            dependencies.AddCommand<FetchDependencyCommand>( "fetch" )
                                .WithData( data )
                                .WithDescription( "Fetch build dependencies from TeamCity but does not update a version that has already been resolved." );

                            dependencies.AddCommand<UpdateDependencyCommand>( "update" )
                                .WithData( data )
                                .WithDescription( "Updates dependencies to the newest version available on TeamCity." );

                            dependencies.AddCommand<UpdateEngineeringCommand>( "update-eng" )
                                .WithData( data )
                                .WithDescription( "Updates PostSharp.Engineering in global.json and Versions.props." );
                        } );

                    root.AddBranch(
                        "codestyle",
                        codestyle =>
                        {
                            codestyle.AddCommand<PushCodeStyleCommand>( "push" )
                                .WithData( data )
                                .WithDescription(
                                    $"Copies the changes in {product.EngineeringDirectory}/shared to the local engineering repo, but does not commit nor push." );

                            codestyle.AddCommand<PullCodeStyleCommand>( "pull" )
                                .WithData( data )
                                .WithDescription(
                                    $"Copies the remote engineering repo to {product.EngineeringDirectory}/shared. Automatically pulls 'master'." );

                            if ( product.Solutions.Any( s => s.CanFormatCode ) )
                            {
                                codestyle.AddCommand<FormatCommand>( "format" )
                                    .WithData( data )
                                    .WithDescription( "Formats the code" );

                                codestyle.AddCommand<InspectCommand>( "inspect" )
                                    .WithData( data )
                                    .WithDescription( "Inspects the code for warnings" );

                                codestyle.AddCommand<ProcessInspectOutputCommand>( "process-inspect-output" )
                                    .WithData( data )
                                    .WithDescription( "Prints errors and warnings for the output of the 'inspect' command" );
                            }
                        } );

                    root.AddBranch(
                        "teamcity",
                        teamcity =>
                        {
                            teamcity.AddCommand<TeamCityBuildCommand>( "run" )
                                .WithData( data )
                                .WithDescription( "Triggers specified build type of specified product on TeamCity." );

                            teamcity.AddBranch(
                                "project",
                                project =>
                                {
                                    project.AddCommand<TeamCityGetProjectDetailsCommand>( "get" )
                                        .WithData( data )
                                        .WithDescription( "Get details of a TeamCity project." );

                                    project.AddCommand<TeamCityCreateProjectCommand>( "create" )
                                        .WithData( data )
                                        .WithDescription( "Creates a new TeamCity project." );

                                    project.AddCommand<TeamCityCreateThisProjectCommand>( "create-this" )
                                        .WithData( data )
                                        .WithDescription(
                                            "Creates a new TeamCity project and VCS root, if it doesn't exist, based on the product in the current repository." );
                                } );

                            teamcity.AddBranch(
                                "vcs-root",
                                vcsRoot =>
                                {
                                    vcsRoot.AddCommand<TeamCityGetVcsRootDetailsCommand>( "get" )
                                        .WithData( data )
                                        .WithDescription( "Get details of a TeamCity VCS root." );

                                    vcsRoot.AddCommand<TeamCityCreateThisVcsRootCommand>( "create-this" )
                                        .WithData( data )
                                        .WithDescription(
                                            "Creates a new TeamCity VCS root, if it doesn't exist, based on the product in the current repository, in a specified project." );
                                } );
                        } );
                }

                root.AddBranch(
                    "tools",
                    tools =>
                    {
                        tools.AddCommand<KillCommand>( "kill" )
                            .WithData( data )
                            .WithDescription( "Kill all compiler processes." );

                        tools.AddCommand<DumpCommand>( "dump" )
                            .WithData( data )
                            .WithDescription( "Dump a given process and all its descendants." );

                        tools.AddCommand<WaitCommand>( "wait" )
                            .WithData( data )
                            .WithDescription( "Wait a given number of seconds. When used to test the behavior of the the --timeout argument." );

                        tools.AddBranch(
                            "csproj",
                            csproj => csproj.AddCommand<AddProjectReferenceCommand>( "add-project-reference" )
                                .WithData( data )
                                .WithDescription( "Adds a <ProjectReference> item to *.csproj in a directory" ) );

                        tools.AddBranch(
                            "msbuild",
                            msbuild => msbuild.AddCommand<ListMSBuildCommand>( "list" )
                                .WithData( data )
                                .WithDescription( "List installed MSBuild instances." ) );

                        tools.AddBranch(
                            "nuget",
                            nuget =>
                            {
                                nuget.AddCommand<RenamePackagesCommand>( "rename" )
                                    .WithDescription( "Renames all packages in a directory" );

                                nuget.AddCommand<VerifyPublicPackageCommand>( "verify-public" )
                                    .WithDescription( "Verifies that all packages in a directory have only references to packages published on nuget.org." );

                                nuget.AddCommand<UnlistNugetPackageCommand>( "unlist" )
                                    .WithDescription( "Unlists package published on nuget.org." );
                            } );

                        tools.AddBranch(
                            "git",
                            git =>
                            {
                                git.AddCommand<GitBulkRenameCommand>( "rename" )
                                    .WithDescription( "Renames all files and directories recursively preserving GIT history." )
                                    .WithExample( @"""C:\src\Caravela.Compiler""", @"""Caravela""", @"""Metalama""" );

                                git.AddCommand<DownstreamMergeCommand>( "merge-downstream" )
                                    .WithData( data )
                                    .WithDescription( "Merges the code to the subsequent development branch." );

                                git.AddCommand<UpstreamCheckCommand>( "check-upstream" )
                                    .WithData( data )
                                    .WithDescription( "Checks the upstream product versions for unmerged changes." );

                                git.AddCommand<SetBranchPoliciesCommand>( "set-branch-policies" )
                                    .WithData( data )
                                    .WithDescription( "Sets the branch policies of the development and release branch of the current product version." );

                                git.AddCommand<PrintBranchPoliciesCommand>( "print-branch-policies" )
                                    .WithData( data )
                                    .WithDescription( "Prints the branch policies currently set for the repository." );

                                git.AddCommand<SetDefaultBranchCommand>( "set-default-branch" )
                                    .WithData( data )
                                    .WithDescription( "Sets the default branch of the repository." );
                            } );

                        tools.AddBranch(
                            "xmldoc",
                            xmldoc => xmldoc.AddCommand<RemoveInternalsCommand>( "clean" ).WithDescription( "Remove internals." ).WithData( data ) );

                        foreach ( var tool in product.DotNetTools )
                        {
                            tools.AddCommand<InvokeDotNetToolCommand>( tool.Alias )
                                .WithData( data )
                                .WithDescription( $"Execute dot net tool '{tool.Command}' from package '{tool.PackageId}' version {tool.Version}." );
                        }
                    } );

                foreach ( var extension in product.Extensions )
                {
                    extension.AddCommands( root, data );
                }
            } );
        }
    }
}