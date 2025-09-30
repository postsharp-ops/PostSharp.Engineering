// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.VisualStudio.Services.Common;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace PostSharp.Engineering.BuildTools.Tools.TeamCity;

public static class TeamCityHelper
{
    public const string TeamCityCloudUrl = "https://postsharp.teamcity.com";
    public const string TeamCityUsername = "teamcity@postsharp.net";
    public const string TeamCityApiBuildQueuePath = $"/app/rest/buildQueue";
    public const string TeamCityApiRunningBuildsPath = "/app/rest/builds?locator=state:running";
    public const string TeamCityApiFinishedBuildsPath = "/app/rest/builds?locator=state:finished";

    public static bool IsTeamCityBuild( CommonCommandSettings? settings )
        => settings?.SimulateContinuousIntegration == true
           || Environment.GetEnvironmentVariable( EnvironmentVariableNames.IsTeamCityAgent )?.ToLowerInvariant() is "true" or "1";

    public static ImmutableDictionary<string, string?> GetSimulatedContinuousIntegrationEnvironmentVariables( CommonCommandSettings settings )
    {
        if ( settings.SimulateContinuousIntegration )
        {
            var isIsTeamCityAgentEnvironmentVariableSet =
                Environment.GetEnvironmentVariable( EnvironmentVariableNames.IsTeamCityAgent )?.ToLowerInvariant() == "true";

            if ( !isIsTeamCityAgentEnvironmentVariableSet )
            {
                return new Dictionary<string, string?> { { EnvironmentVariableNames.IsTeamCityAgent, "true" } }.ToImmutableDictionary();
            }
        }

        return ImmutableDictionary<string, string?>.Empty;
    }

    public static bool TryConnectTeamCity( CiProjectConfiguration configuration, ConsoleHelper console, [NotNullWhen( true )] out TeamCityClient? client )
    {
        var teamcityTokenVariable = configuration.TokenEnvironmentVariableName;

        var token = Environment.GetEnvironmentVariable( teamcityTokenVariable );

        if ( string.IsNullOrEmpty( token ) )
        {
            console.WriteError( $"The {teamcityTokenVariable} environment variable is not defined." );
            client = null;

            return false;
        }

        client = new TeamCityClient( configuration.BaseUrl, token );

        return true;
    }

    public static bool TryConnectTeamCity( BuildContext context, [NotNullWhen( true )] out TeamCityClient? client )
        => TryConnectTeamCity( context.Product.DependencyDefinition.CiConfiguration, context.Console, out client );

    public static bool TryGetTeamCitySourceWriteToken( out string environmentVariableName, [NotNullWhen( true )] out string? teamCitySourceWriteToken )
    {
        environmentVariableName = "SOURCE_CODE_WRITING_TOKEN";
        teamCitySourceWriteToken = Environment.GetEnvironmentVariable( environmentVariableName );

        if ( teamCitySourceWriteToken == null )
        {
            return false;
        }

        return true;
    }

    public static string GetTeamCitySourceReadToken()
    {
        // We use TeamCity configuration parameter value.
        const string buildParameterName = "SOURCE_CODE_READING_TOKEN";
        var teamCitySourceReadToken = Environment.GetEnvironmentVariable( buildParameterName );

        if ( string.IsNullOrWhiteSpace( teamCitySourceReadToken ) )
        {
            throw new InvalidOperationException( $"The '{buildParameterName}' environment variable is not defined." );
        }

        return teamCitySourceReadToken;
    }

    public static bool TriggerTeamCityBuild(
        BuildContext context,
        CommonCommandSettings settings,
        string productFamilyName,
        string productFamilyVersion,
        string productName,
        TeamCityBuildType buildType,
        BuildConfiguration? buildConfiguration )
    {
        if ( !TryGetDependencyDefinition( context, productFamilyName, productFamilyVersion, productName, out var dependencyDefinition ) )
        {
            return false;
        }

        if ( !TryGetBuildTypeId( context, dependencyDefinition, buildType, buildConfiguration, out var buildTypeId ) )
        {
            return false;
        }

        if ( !TryConnectTeamCity( dependencyDefinition.CiConfiguration, context.Console, out var tc ) )
        {
            return false;
        }

        var scheduledBuildId = tc.ScheduleBuild( context.Console, buildTypeId, "This build was triggered by command." );

        if ( string.IsNullOrEmpty( scheduledBuildId ) )
        {
            context.Console.WriteError( $"Failed to schedule build '{buildTypeId}'." );

            return false;
        }

        context.Console.WriteMessage( $"Scheduling build '{buildTypeId}' with build ID '{scheduledBuildId}'." );

        var scheduledBuildNumber = string.Empty;

        void ClearLine( int length )
        {
            // TeamCity doesn't allow moving cursor up to rewrite the line as its build log is only a console output.
            if ( !IsTeamCityBuild( settings ) && context.Console.Out != null )
            {
                context.Console.Out.Cursor.MoveUp();
                context.Console.Out.WriteLine( new string( ' ', length ) );
                context.Console.Out.Cursor.MoveUp();
            }
        }

        if ( tc.IsBuildQueued( context.Console, scheduledBuildId ) )
        {
            var message = "Waiting for build to start...";
            context.Console.WriteMessage( message );

            while ( tc.IsBuildQueued( context.Console, scheduledBuildId ) )
            {
                Thread.Sleep( 5000 );
            }

            ClearLine( message.Length );
        }

        // Poll the running build status until it is finished.
        while ( !tc.HasBuildFinished( context.Console, scheduledBuildId ) )
        {
            var message = tc.PollRunningBuildStatus( scheduledBuildId, out var buildNumber );
            context.Console.WriteMessage( message );
            scheduledBuildNumber = buildNumber;

            Thread.Sleep( 5000 );

            ClearLine( message.Length );
        }

        if ( !tc.HasBuildFinishedSuccessfully( context.Console, scheduledBuildId ) )
        {
            context.Console.WriteError( $"Build #{scheduledBuildNumber} of '{buildTypeId}' failed." );

            return false;
        }

        context.Console.WriteSuccess( $"Build #{scheduledBuildNumber} of '{buildTypeId}' has finished successfully." );

        return true;
    }

    private static bool TryGetDependencyDefinition(
        BuildContext context,
        string productFamilyName,
        string productFamilyVersion,
        string productName,
        [NotNullWhen( true )] out DependencyDefinition? dependencyDefinition )
    {
        dependencyDefinition = null;

        if ( !ProductFamily.TryGetFamily( productFamilyName, productFamilyVersion, out var productFamily ) )
        {
            context.Console.WriteError( $"Unknown product family '{productFamilyName}' version '{productFamilyVersion}'." );

            return false;
        }

        if ( !productFamily.TryGetDependencyDefinition( productName, out dependencyDefinition ) )
        {
            context.Console.WriteError(
                $"Dependency definition '{productName}' not found in '{productFamily.Name}' product family version '{productFamily.Version}'." );

            return false;
        }

        return true;
    }

    private static bool TryGetBuildTypeId(
        BuildContext context,
        DependencyDefinition dependencyDefinition,
        TeamCityBuildType buildType,
        BuildConfiguration? buildConfiguration,
        [NotNullWhen( true )] out string? ciBuildTypeId )
    {
        ciBuildTypeId = null;

        // We get the required build type ID of the product from the DependencyDefinition.
        switch ( buildType )
        {
            case TeamCityBuildType.Build:
                if ( buildConfiguration == null )
                {
                    context.Console.WriteError( "It is required to specify the build configuration for building a project." );

                    return false;
                }

                ciBuildTypeId = dependencyDefinition.CiConfiguration.BuildTypes[buildConfiguration.Value];

                break;

            case TeamCityBuildType.Deploy:
                ciBuildTypeId = dependencyDefinition.CiConfiguration.DeploymentBuildType;

                break;

            case TeamCityBuildType.Bump:
                ciBuildTypeId = dependencyDefinition.CiConfiguration.VersionBumpBuildType;

                break;

            default:
                ciBuildTypeId = null;

                break;
        }

        if ( ciBuildTypeId == null )
        {
            context.Console.WriteError( $"'{dependencyDefinition.Name}' product has no known build type ID for build type '{buildType}'." );

            return false;
        }

        return true;
    }

    public static CiProjectConfiguration CreateConfiguration(
        TeamCityProjectId teamCityProjectId,
        bool hasVersionBump = true,
        bool pullRequestRequiresStatusCheck = true,
        string? pullRequestStatusCheckBuildType = null,
        string? vcsRootProjectId = null )
    {
        var buildTypes = new ConfigurationSpecific<string>(
            $"{teamCityProjectId}_DebugBuild",
            $"{teamCityProjectId}_ReleaseBuild",
            $"{teamCityProjectId}_PublicBuild" );

        string? versionBumpBuildType = null;

        if ( hasVersionBump )
        {
            versionBumpBuildType = $"{teamCityProjectId}_VersionBump";
        }

        var deploymentBuildType = $"{teamCityProjectId}_PublicDeployment";

        var tokenEnvironmentVariableName = EnvironmentVariableNames.TeamCityToken;
        var baseUrl = TeamCityCloudUrl;

        return new CiProjectConfiguration(
            teamCityProjectId,
            buildTypes,
            deploymentBuildType,
            versionBumpBuildType,
            tokenEnvironmentVariableName,
            baseUrl,
            pullRequestRequiresStatusCheck,
            pullRequestStatusCheckBuildType,
            vcsRootProjectId );
    }

    private static string ReplaceDots( string input, string substitute = "" ) => input.Replace( ".", substitute, StringComparison.Ordinal );

    private static string ReplaceSpaces( string input, string substitute = "" ) => input.Replace( " ", substitute, StringComparison.Ordinal );

    private static string GetProjectIdFromName( string projectName ) => ReplaceDots( ReplaceSpaces( projectName ) );

    public static TeamCityProjectId GetProjectIdWithParentProjectId( string projectName, string? parentProjectId )
    {
        var subProjectId = GetProjectIdFromName( projectName );

        var projectId = parentProjectId == null ? subProjectId : $"{parentProjectId}_{subProjectId}";

        return new TeamCityProjectId( projectId, parentProjectId ?? "_Root" );
    }

    public static TeamCityProjectId GetProjectId( string projectName, string? parentProjectName = null, string? productFamilyVersion = null )
    {
        string parentProjectId;

        if ( parentProjectName == null )
        {
            parentProjectId = "";
        }
        else
        {
            parentProjectId = GetProjectIdFromName( parentProjectName );

            if ( productFamilyVersion != null )
            {
                parentProjectId = $"{parentProjectId}_{parentProjectId}{ReplaceDots( productFamilyVersion )}";
            }
        }

        var projectId = GetProjectIdWithParentProjectId( projectName, parentProjectId );

        return projectId;
    }

    public static void SendImportDataMessage( string type, string path, string flowId, bool failOnNoData )
    {
        var date = DateTimeOffset.Now;
        var timestamp = $"{date:yyyy-MM-dd'T'HH:mm:ss.fff}{date.Offset.Ticks:+;-;}{date.Offset:hhmm}";

        Console.WriteLine(
            "##teamcity[importData type='{0}' path='{1}' flowId='{2}' timestamp='{3}' whenNoDataPublished='{4}']",
            type,
            path,
            flowId,
            timestamp,
            failOnNoData ? "error" : "warning" );
    }

    private static bool TryCreateProject( TeamCityClient tc, BuildContext context, string name, string id, string? parentId = null, string? vcsRootId = null )
    {
        context.Console.WriteMessage( $"Creating project '{name}', ID '{id}', parent project ID '{parentId}'." );

        if ( !tc.TryCreateProject( context.Console, name, id, parentId ) )
        {
            return false;
        }

        if ( vcsRootId != null )
        {
            context.Console.WriteMessage( $"Setting versioned settings for project '{name}' ('{id}') using VCS root ID '{vcsRootId}'." );

            if ( !tc.TrySetProjectVersionedSettings( context.Console, id, vcsRootId ) )
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryCreateProject( BuildContext context, string name, string id, string? parentId = null, string? vcsRootId = null )
    {
        if ( !TryConnectTeamCity( context, out var tc ) )
        {
            return false;
        }

        return TryCreateProject( tc, context, name, id, parentId, vcsRootId );
    }

    public static string GetVcsId( DependencyDefinition dependencyDefinition )
        => GetVcsId( dependencyDefinition.VcsRepository, dependencyDefinition.CiConfiguration.VcsRootProjectId );

    private static string GetVcsId( VcsRepository repository, string? projectId )
        => $"{projectId ?? "Root"}_{repository.Name.Replace( ".", "", StringComparison.Ordinal )}";

    private static bool TryCreateVcsRoot( TeamCityClient tc, BuildContext context, string? projectId, out string vcsRootId )
    {
        var repository = context.Product.DependencyDefinition.VcsRepository;
        vcsRootId = GetVcsId( repository, projectId );

        context.Console.WriteMessage( $"Retrieving VCS roots of '{projectId}' project." );

        if ( !tc.TryGetVcsRoots( context.Console, projectId ?? "_Root", out var vcsRoots ) )
        {
            return false;
        }

        if ( vcsRoots.Value.ToHashSet( r => r.Id ).Contains( vcsRootId ) )
        {
            context.Console.WriteMessage( $"Using existing '{vcsRootId}' VCS root" );

            return true;
        }

        var vcsRootName = repository.Name;
        var familyVersion = context.Product.ProductFamily.Version;
        var defaultBranch = context.Product.DependencyDefinition.Branch;

        var branchSpecification = new List<string>
        {
            $"+:refs/heads/(topic/{familyVersion}/*)",
            $"+:refs/heads/(feature/{familyVersion}/*)",
            $"+:refs/heads/(experimental/{familyVersion}/*)",
            $"+:refs/heads/(merge/{familyVersion}/*)",
            $"+:refs/heads/({context.Product.DependencyDefinition.Branch})"
        };

        if ( context.Product.DependencyDefinition.ReleaseBranch != null )
        {
            branchSpecification.Add( $"+:refs/heads/({context.Product.DependencyDefinition.ReleaseBranch})" );
        }

        context.Console.WriteMessage(
            $"Creating '{repository.TeamCityRemoteUrl}' VCS root in '{projectId}' project for '{familyVersion}' family version with default branch '{defaultBranch}'." );

        if ( !tc.TryCreateVcsRoot( context.Console, projectId, vcsRootId, vcsRootName, defaultBranch, repository, branchSpecification ) )
        {
            return false;
        }

        context.Console.WriteMessage( $"Created '{vcsRootName}' VCS root ID '{vcsRootId}'." );

        return true;
    }

    public static bool TryCreateVcsRoot( BuildContext context, string? projectId )
    {
        if ( !TryConnectTeamCity( context, out var tc ) )
        {
            return false;
        }

        if ( !TryCreateVcsRoot( tc, context, projectId, out _ ) )
        {
            return false;
        }

        if ( !TryCreateVcsRoot( tc, context, projectId, out _ ) )
        {
            return false;
        }

        return true;
    }

    public static bool TryCreateProject( BuildContext context )
    {
        if ( !TryConnectTeamCity( context, out var tc ) )
        {
            return false;
        }

        var projectId = context.Product.DependencyDefinition.CiConfiguration.ProjectId.Id;
        var parentProjectId = context.Product.DependencyDefinition.CiConfiguration.ProjectId.ParentId;
        var vcsRootProjectId = context.Product.DependencyDefinition.CiConfiguration.VcsRootProjectId;
        var projectName = context.Product.DependencyDefinition.Name;

        if ( !TryCreateVcsRoot( tc, context, vcsRootProjectId, out _ ) )
        {
            return false;
        }

        if ( !TryCreateVcsRoot( tc, context, vcsRootProjectId, out var vcsRootId ) )
        {
            return false;
        }

        if ( !TryCreateProject( tc, context, projectName, projectId, parentProjectId, vcsRootId ) )
        {
            return false;
        }

        return true;
    }
}