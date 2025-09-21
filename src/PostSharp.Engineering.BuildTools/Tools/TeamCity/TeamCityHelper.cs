// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.VisualStudio.Services.Common;
using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Triggers;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.Arguments;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Utilities;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
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

    /// <summary>
    /// Attempts to set Git user credentials to TeamCity. Configurations are set only for the current repository.
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    public static bool TrySetGitIdentityCredentials( BuildContext context )
    {
        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                "config user.name TeamCity",
                context.RepoDirectory ) )
        {
            return false;
        }

        if ( !ToolInvocationHelper.InvokeTool(
                context.Console,
                "git",
                $"config user.email {TeamCityUsername}",
                context.RepoDirectory ) )
        {
            return false;
        }

        return true;
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

    public static string GetVcsRootId( DependencyDefinition dependencyDefinition, bool parameterizedDefaultBranch = true )
        => GetVcsRootId( dependencyDefinition.VcsRepository, dependencyDefinition.CiConfiguration.VcsRootProjectId, parameterizedDefaultBranch );

    private static string GetVcsRootId( VcsRepository repository, string? projectId, bool parameterizedDefaultBranch )
        => $"{projectId ?? "Root"}_{repository.Name.Replace( ".", "", StringComparison.Ordinal )}{(parameterizedDefaultBranch ? "" : "_Default")}";

    private static bool TryCreateVcsRoot( TeamCityClient tc, BuildContext context, string? projectId, bool parameterizedDefaultBranch, out string vcsRootId )
    {
        var repository = context.Product.DependencyDefinition.VcsRepository;
        vcsRootId = GetVcsRootId( repository, projectId, parameterizedDefaultBranch );

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

        var vcsRootName = parameterizedDefaultBranch ? repository.Name : $"{repository.Name}_Default";
        var familyVersion = context.Product.ProductFamily.Version;
        var defaultBranch = parameterizedDefaultBranch ? $"%{repository.DefaultBranchParameter}%" : context.Product.DependencyDefinition.Branch;

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

        if ( !TryCreateVcsRoot( tc, context, projectId, true, out _ ) )
        {
            return false;
        }

        if ( !TryCreateVcsRoot( tc, context, projectId, false, out _ ) )
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

        if ( !TryCreateVcsRoot( tc, context, vcsRootProjectId, true, out _ ) )
        {
            return false;
        }

        if ( !TryCreateVcsRoot( tc, context, vcsRootProjectId, false, out var vcsRootId ) )
        {
            return false;
        }

        if ( !TryCreateProject( tc, context, projectName, projectId, parentProjectId, vcsRootId ) )
        {
            return false;
        }

        return true;
    }

    internal static void GeneratePom( BuildContext context, string projectObjectName, string tcUrl )
        => WriteIfDiffers(
            context,
            w => w.Write(
                @$"<?xml version=""1.0""?>
<project>
  <modelVersion>4.0.0</modelVersion>
  <name>{projectObjectName} Config DSL Script</name>
  <groupId>{projectObjectName}</groupId>
  <artifactId>{projectObjectName}_dsl</artifactId>
  <version>1.0-SNAPSHOT</version>

  <parent>
    <groupId>org.jetbrains.teamcity</groupId>
    <artifactId>configs-dsl-kotlin-parent</artifactId>
    <version>1.0-SNAPSHOT</version>
  </parent>

  <repositories>
    <repository>
      <id>jetbrains-all</id>
      <url>https://download.jetbrains.com/teamcity-repository</url>
      <snapshots>
        <enabled>true</enabled>
      </snapshots>
    </repository>
    <repository>
      <id>teamcity-server</id>
      <url>{tcUrl}/app/dsl-plugins-repository</url>
      <snapshots>
        <enabled>true</enabled>
      </snapshots>
    </repository>
  </repositories>

  <pluginRepositories>
    <pluginRepository>
      <id>JetBrains</id>
      <url>https://download.jetbrains.com/teamcity-repository</url>
    </pluginRepository>
  </pluginRepositories>

  <build>
    <sourceDirectory>${{basedir}}</sourceDirectory>
    <plugins>
      <plugin>
        <artifactId>kotlin-maven-plugin</artifactId>
        <groupId>org.jetbrains.kotlin</groupId>
        <version>${{kotlin.version}}</version>

        <configuration/>
        <executions>
          <execution>
            <id>compile</id>
            <phase>process-sources</phase>
            <goals>
              <goal>compile</goal>
            </goals>
          </execution>
          <execution>
            <id>test-compile</id>
            <phase>process-test-sources</phase>
            <goals>
              <goal>test-compile</goal>
            </goals>
          </execution>
        </executions>
      </plugin>
      <plugin>
        <groupId>org.jetbrains.teamcity</groupId>
        <artifactId>teamcity-configs-maven-plugin</artifactId>
        <version>${{teamcity.dsl.version}}</version>
        <configuration>
          <format>kotlin</format>
          <dstDir>target/generated-configs</dstDir>
        </configuration>
      </plugin>
    </plugins>
  </build>

  <dependencies>
    <dependency>
      <groupId>org.jetbrains.teamcity</groupId>
      <artifactId>configs-dsl-kotlin-latest</artifactId>
      <version>${{teamcity.dsl.version}}</version>
      <scope>compile</scope>
    </dependency>
    <dependency>
      <groupId>org.jetbrains.teamcity</groupId>
      <artifactId>configs-dsl-kotlin-plugins-latest</artifactId>
      <version>1.0-SNAPSHOT</version>
      <type>pom</type>
      <scope>compile</scope>
    </dependency>
    <dependency>
      <groupId>org.jetbrains.kotlin</groupId>
      <artifactId>kotlin-stdlib-jdk8</artifactId>
      <version>${{kotlin.version}}</version>
      <scope>compile</scope>
    </dependency>
    <dependency>
      <groupId>org.jetbrains.kotlin</groupId>
      <artifactId>kotlin-script-runtime</artifactId>
      <version>${{kotlin.version}}</version>
      <scope>compile</scope>
    </dependency>
  </dependencies>
</project>" ),
            "Project object model",
            Path.Combine( context.RepoDirectory, ".teamcity", "pom.xml" ) );

    internal static void GenerateTeamCityConfiguration( BuildContext context, TeamCityProject project )
    {
        var content = new StringWriter();
        project.GenerateTeamcityCode( content );

        var filePath = Path.Combine( context.RepoDirectory, ".teamcity", "settings.kts" );

        WriteIfDiffers( context, project.GenerateTeamcityCode, "Continuous integration script", filePath );
    }

    private static void WriteIfDiffers( BuildContext context, Action<TextWriter> writer, string name, string filePath )
    {
        var content = new StringWriter();
        writer( content );

        string resultMessage;

        if ( !File.Exists( filePath ) || File.ReadAllText( filePath ) != content.ToString() )
        {
            context.Console.WriteWarning( $"Replacing '{filePath}'." );
            File.WriteAllText( filePath, content.ToString() );
            resultMessage = $"File '{filePath}' was written.";
        }
        else
        {
            resultMessage = $"File '{filePath}' was up to date.";
        }

        context.Console.WriteMessage( $"{name} generated. {resultMessage}" );
    }

    internal static bool TryGenerateConsolidatedTeamcityConfiguration( BuildContext context )
    {
        // This method is implemented so it preserves the order of all entities in the resulting script.

        if ( !TryConnectTeamCity( context, out var tc ) )
        {
            return false;
        }

        var product = context.Product;
        var consolidatedProjectId = product.DependencyDefinition.CiConfiguration.ProjectId;
        var consolidatedProjectIdPrefix = $"{consolidatedProjectId}_";
        var defaultBranch = product.DependencyDefinition.Branch;
        var deploymentBranch = product.DependencyDefinition.ReleaseBranch;
        var defaultBranchParameter = product.DependencyDefinition.VcsRepository.DefaultBranchParameter;
        var vcsRootId = GetVcsRootId( product.DependencyDefinition );

        if ( deploymentBranch == null )
        {
            context.Console.WriteError( $"Release branch not set for the consolidated project." );

            return false;
        }

        var tcConfigurations = new List<TeamCityBuildConfiguration>();
        var nuGetConfigurations = new List<TeamCityBuildConfiguration>();

        if ( !tc.TryGetOrderedSubprojectsRecursively(
                context.Console,
                consolidatedProjectId.ParentId,
                out var subprojects ) )
        {
            return false;
        }

        var consolidatedProjectName = product.ProductName;

        var buildConfigurations = new List<(string ProjectId, string ProjectName, string BuildConfigurationId, HashSet<string> SnapshotDependencies)>();

        var buildConfigurationsById =
            new Dictionary<string, (string ProjectId, string ProjectName, string BuildConfigurationId, HashSet<string> SnapshotDependencies)>();

        var buildConfigurationsByKind =
            new Dictionary<string, List<(string ProjectId, string ProjectName, string BuildConfigurationId, HashSet<string> SnapshotDependencies)>>();

        // Exclude the consolidated build project.
        subprojects = subprojects.Where( p => !p.Id.EndsWith( $"_{consolidatedProjectName}", StringComparison.Ordinal ) ).ToImmutableArray();

        foreach ( var project in subprojects )
        {
            if ( !tc.TryGetProjectsBuildConfigurations( context.Console, project.Id, out var projectsBuildConfigurations ) )
            {
                return false;
            }

            foreach ( var buildConfigurationId in projectsBuildConfigurations )
            {
                if ( !tc.TryGetBuildConfigurationsSnapshotDependencies( context.Console, buildConfigurationId, out var snapshotDependencies ) )
                {
                    return false;
                }

                var buildConfigurationKind = buildConfigurationId.Split( '_' ).Last();

                if ( !buildConfigurationsByKind.TryGetValue( buildConfigurationKind, out var buildConfigurationsOfKind ) )
                {
                    buildConfigurationsOfKind =
                        new List<(string ProjectId, string ProjectName, string BuildConfigurationId, HashSet<string> SnapshotDependencies)>();

                    buildConfigurationsByKind.Add( buildConfigurationKind, buildConfigurationsOfKind );
                }

                var buildConfiguration = (project.Id, project.Name, buildConfigurationId, snapshotDependencies.Value.ToHashSet());
                buildConfigurations.Add( buildConfiguration );
                buildConfigurationsOfKind.Add( buildConfiguration );
                buildConfigurationsById.Add( buildConfigurationId, buildConfiguration );
            }
        }

        // TeamCity doesn't allow to have artifact dependencies that match no artifacts.
        // TODO: Make this configurable.
        string[] projectsWithNoNuGetArtifacts = [".Vsx", ".Documentation", ".Try", ".Tests."];

        static string MarkNuGetObjectId( string objectId ) => $"NuGet{objectId}";

        bool TryPopulateBuildConfigurations(
            BuildConfiguration configuration,
            string consolidatedBuildObjectName,
            string consolidatedBuildConfigurationName,
            IBuildTrigger[] consolidatedBuildTriggers,
            string nuGetBuildObjectName,
            string nuGetBuildConfigurationName,
            [NotNullWhen( true )] out TeamCityBuildConfiguration? consolidatedBuildConfiguration,
            [NotNullWhen( true )] out TeamCityBuildConfiguration? nuGetBuildConfiguration,
            out Dictionary<string, HashSet<string>> dependenciesByProjectId,
            out List<TeamCitySnapshotDependency> nuGetDependencies,
            [NotNullWhen( true )] out string? buildArtifactRules )
        {
            List<TeamCitySnapshotDependency> dependencies = new();
            consolidatedBuildConfiguration = null;
            nuGetBuildConfiguration = null;
            dependenciesByProjectId = new Dictionary<string, HashSet<string>>();
            nuGetDependencies = new List<TeamCitySnapshotDependency>();
            buildArtifactRules = null;

            foreach ( var buildConfiguration in buildConfigurationsByKind[consolidatedBuildObjectName] )
            {
                var dependencyProjectId = buildConfiguration.ProjectId;

                if ( !product.DependencyDefinition.ProductFamily.TryGetDependencyDefinitionByCiId( dependencyProjectId, out var dependencyDefinition ) )
                {
                    context.Console.WriteError( $"Dependency definition for project '{dependencyProjectId}' not found." );

                    return false;
                }

                if ( dependencyDefinition.ProductFamily == product.ProductFamily
                     && !projectsWithNoNuGetArtifacts.Any( p => dependencyDefinition.Name.Contains( p, StringComparison.Ordinal ) ) )
                {
                    var dependencyMsBuildConfiguration = dependencyDefinition.MSBuildConfiguration[configuration];
                    var dependencyBuildInfo = new BuildInfo( null, configuration.ToString(), dependencyMsBuildConfiguration, null );

                    var dependencyPrivateArtifactsDirectory = dependencyDefinition.PrivateArtifactsDirectory.ToString( dependencyBuildInfo )
                        .Replace( Path.DirectorySeparatorChar, '/' );

                    var dependencyPublicArtifactsDirectory = dependencyDefinition.PublicArtifactsDirectory.ToString( dependencyBuildInfo )
                        .Replace( Path.DirectorySeparatorChar, '/' );

                    var dependencyName = dependencyDefinition.Name;
                    var artifactRulesFormat = $"+:{{0}}/**/*.{{1}}=>dependencies/{dependencyName}";

                    var packagesArtifactsDirectory = configuration switch
                    {
                        BuildConfiguration.Public => dependencyPublicArtifactsDirectory,
                        _ => dependencyPrivateArtifactsDirectory
                    };

                    string[] rules =
                    [
                        string.Format( CultureInfo.InvariantCulture, artifactRulesFormat, dependencyPrivateArtifactsDirectory, "version.props" ),
                        string.Format( CultureInfo.InvariantCulture, artifactRulesFormat, packagesArtifactsDirectory, "nupkg" ),
                        string.Format( CultureInfo.InvariantCulture, artifactRulesFormat, packagesArtifactsDirectory, "snupkg" )
                    ];

                    var artifactRules = string.Join( "\\n", rules );

                    nuGetDependencies.Add(
                        new TeamCitySnapshotDependency(
                            buildConfiguration.BuildConfigurationId,
                            true,
                            artifactRules ) );
                }

                dependencies.Add(
                    new TeamCitySnapshotDependency(
                        buildConfiguration.BuildConfigurationId,
                        true ) );

                if ( !dependenciesByProjectId.TryGetValue( buildConfiguration.BuildConfigurationId, out var projectDependencies ) )
                {
                    projectDependencies = new HashSet<string>();
                    dependenciesByProjectId.Add( buildConfiguration.ProjectId, projectDependencies );
                }

                // We check for presence, because some dependencies can come from other project families.
                // E.g. PostSharp for Metalama.Vsx.
                projectDependencies.AddRange(
                    buildConfiguration.SnapshotDependencies.Select( d => buildConfigurationsById.TryGetValue( d, out var c ) ? c.ProjectId : null )
                        .Where( c => c != null )
                        .Select( c => c! ) );
            }

            var buildInfo = new BuildInfo( null, configuration, product, null );

            var privateArtifactsDirectory =
                product.PrivateArtifactsDirectory.ToString( buildInfo ).Replace( "\\", "/", StringComparison.Ordinal );

            var publicArtifactsDirectory =
                product.PublicArtifactsDirectory.ToString( buildInfo ).Replace( "\\", "/", StringComparison.Ordinal );

            buildArtifactRules =
                $@"+:{privateArtifactsDirectory}/**/*=>{privateArtifactsDirectory}\n+:{publicArtifactsDirectory}/**/*=>{publicArtifactsDirectory}";

            var nuGetBuildCiId = $"{consolidatedProjectIdPrefix}{nuGetBuildObjectName}";

            dependencies.Add( new TeamCitySnapshotDependency( nuGetBuildCiId, true ) );

            var defaultBuildBranch = configuration switch
            {
                // We should use deploymentBranch here, but TeamCity doesn't support parameterized branches in snapshot dependencies.
                BuildConfiguration.Public => defaultBranch,
                _ => defaultBranch
            };

            consolidatedBuildConfiguration =
                new TeamCityBuildConfiguration(
                    consolidatedBuildObjectName,
                    consolidatedBuildConfigurationName,
                    defaultBuildBranch,
                    defaultBranchParameter,
                    vcsRootId ) { SnapshotDependencies = dependencies.ToArray(), BuildTriggers = consolidatedBuildTriggers };

            ContainerImageSpec? dockerSpec = null;

            if ( product.UseDocker )
            {
                dockerSpec = new ContainerImageSpec( $"{product.ProductNameWithoutDot}-{product.ProductFamily.Version}" );
            }

            var nuGetBuildSteps =
                new TeamCityBuildStep[] { new TeamCityEngineeringBuildBuildStep( configuration, false, dockerSpec ) };

            // The default branch is the same as for public build of any other project - see the build configuration of a regular project.
            nuGetBuildConfiguration = new TeamCityBuildConfiguration(
                nuGetBuildObjectName,
                nuGetBuildConfigurationName,
                defaultBranch,
                defaultBranchParameter,
                vcsRootId,
                product.ResolvedBuildAgentRequirements )
            {
                BuildSteps = nuGetBuildSteps,
                SnapshotDependencies = nuGetDependencies.ToArray(),
                ArtifactRules = buildArtifactRules,
                BuildTimeOutThreshold = product.BuildTimeOutThreshold
            };

            return true;
        }

        // Debug Build
        const string debugBuildObjectName = "DebugBuild";
        const string debugBuildName = "Build [Debug]";

        if ( !TryPopulateBuildConfigurations(
                BuildConfiguration.Debug,
                debugBuildObjectName,
                debugBuildName,
                [],
                MarkNuGetObjectId( debugBuildObjectName ),
                debugBuildName,
                out var consolidatedDebugBuildConfiguration,
                out var nuGetDebugBuildConfiguration,
                out _,
                out _,
                out _ ) )
        {
            return false;
        }

        tcConfigurations.Add( consolidatedDebugBuildConfiguration );
        nuGetConfigurations.Add( nuGetDebugBuildConfiguration );

        // Downstream Merge

        // Downstream merge of the consolidated build repo itself needs to be done manually,
        // because the TeamCity script needs to be regenerated in each product family version. 

        const string downstreamMergeObjectName = "DownstreamMerge";

        if ( buildConfigurationsByKind.TryGetValue( downstreamMergeObjectName, out var downstreamMergeBuildConfigurations ) )
        {
            var consolidatedDownstreamMergeSnapshotDependencies =
                downstreamMergeBuildConfigurations.Select( c => new TeamCitySnapshotDependency( c.BuildConfigurationId, true ) );

            var consolidatedDownstreamMergeBuildTriggers = new IBuildTrigger[] { new NightlyBuildTrigger( 23, true ) };

            tcConfigurations.Add(
                new TeamCityBuildConfiguration( downstreamMergeObjectName, "Merge Downstream", defaultBranch, defaultBranchParameter, vcsRootId )
                {
                    SnapshotDependencies = consolidatedDownstreamMergeSnapshotDependencies.ToArray(),
                    BuildTriggers = consolidatedDownstreamMergeBuildTriggers
                } );
        }

        // Release Build
        const string releaseBuildObjectName = "ReleaseBuild";
        const string releaseBuildName = "Build [Release]";

        if ( !TryPopulateBuildConfigurations(
                BuildConfiguration.Release,
                releaseBuildObjectName,
                releaseBuildName,
                [],
                MarkNuGetObjectId( releaseBuildObjectName ),
                releaseBuildName,
                out var consolidatedReleaseBuildConfiguration,
                out var nuGetReleaseBuildConfiguration,
                out _,
                out _,
                out _ ) )
        {
            return false;
        }

        tcConfigurations.Add( consolidatedReleaseBuildConfiguration );
        nuGetConfigurations.Add( nuGetReleaseBuildConfiguration );

        // Version bump and public build
        const string publicBuildObjectName = "PublicBuild";
        const string publicBuildName = "Build [Public]";
        var publicConfiguration = BuildConfiguration.Public;

        if ( !TryPopulateBuildConfigurations(
                BuildConfiguration.Public,
                publicBuildObjectName,
                $"3. {publicBuildName}",
                [
                    new NightlyBuildTrigger( 2, true )
                    {
                        // The nightly build is done on the develop branch to find issues early and to prepare for the deployment.
                        // The manually triggered build is done on the release branch to allow for deployment without merge freeze.
                        // Any successful build of the same commit done on the develop branch is reused by TeamCity when deploying from the release branch.
                        BranchFilter = $"+:{defaultBranch}", Parameters = [new TeamCityBuildConfigurationParameter( "DefaultBranch", defaultBranch )]
                    }
                ],
                MarkNuGetObjectId( publicBuildObjectName ),
                publicBuildName,
                out var consolidatedPublicBuildConfiguration,
                out var nuGetPublicBuildConfiguration,
                out var consolidatedPublicBuildSnapshotDependenciesByProjectId,
                out var nuGetPublicBuildDependencies,
                out var nuGetBuildArtifactRules ) )
        {
            return false;
        }

        const string versionBumpObjectName = "VersionBump";

        var consolidatedVersionBumpSteps = new List<TeamCityBuildStep>();
        var consolidatedVersionBumpSourceDependencies = new List<TeamCitySourceDependency>();
        var bumpedProjects = new HashSet<string>();
        var consolidatedVersionBumpParameters = new List<TeamCityBuildConfigurationParameterBase>();

        var success = true;

        TeamCitySourceDependency CreateSourceDependency( string vcsProjectRootId, string projectName )
            => new( vcsProjectRootId, true, $"+:. => {product.SourceDependenciesDirectory}/{projectName}" );

        TeamCitySourceDependency CreateSourceDependencyFromDefinition( DependencyDefinition dependencyDefinition )
            => CreateSourceDependency( GetVcsRootId( dependencyDefinition ), dependencyDefinition.Name );

        foreach ( var buildConfiguration in buildConfigurationsByKind[publicBuildObjectName] )
        {
            var bumpedProjectId = buildConfiguration.ProjectId;
            var bumpedProjectName = buildConfiguration.ProjectName;

            if ( !product.DependencyDefinition.ProductFamily.TryGetDependencyDefinitionByCiId( bumpedProjectId, out var dependencyDefinition ) )
            {
                context.Console.WriteError( $"Dependency definition for project '{bumpedProjectId}' not found." );

                return false;
            }

            if ( !dependencyDefinition.IsVersioned )
            {
                continue;
            }

            foreach ( var projectDependencyId in consolidatedPublicBuildSnapshotDependenciesByProjectId[bumpedProjectId] )
            {
                if ( !bumpedProjects.Contains( projectDependencyId ) )
                {
                    context.Console.WriteError( $"Incorrect projects order. '{bumpedProjectId}' depends on '{projectDependencyId}', but is ordered earlier." );
                    success = false;
                }
            }

            consolidatedVersionBumpSteps.Add(
                new TeamCityEngineeringCommandBuildStep(
                    $"Bump{bumpedProjectId.Split( '_' ).Last()}",
                    $"Bump version of {bumpedProjectName}",
                    "bump",
                    areCustomArgumentsAllowed: true ) { WorkingDirectory = $"source-dependencies/{bumpedProjectName}" } );

            consolidatedVersionBumpSourceDependencies.Add( CreateSourceDependencyFromDefinition( dependencyDefinition ) );

            if ( dependencyDefinition.VcsRepository.DefaultBranchParameter != VcsRepository.DefaultDefaultBranchParameter )
            {
                consolidatedVersionBumpParameters.Add(
                    new TeamCityTextBuildConfigurationParameterBase(
                        dependencyDefinition.VcsRepository.DefaultBranchParameter,
                        dependencyDefinition.VcsRepository.DefaultBranchParameter,
                        $"Default branch of {bumpedProjectName}",
                        dependencyDefinition.Branch ) );
            }

            bumpedProjects.Add( bumpedProjectId );
        }

        if ( !success )
        {
            return false;
        }

        var consolidatedVersionBumpBuildTriggers = new IBuildTrigger[] { new NightlyBuildTrigger( 1, false ) };

        tcConfigurations.Add(
            new TeamCityBuildConfiguration(
                versionBumpObjectName,
                "1. Version Bump",
                defaultBranch,
                defaultBranchParameter,
                vcsRootId,
                product.ResolvedBuildAgentRequirements )
            {
                BuildSteps = consolidatedVersionBumpSteps.ToArray(),
                BuildTriggers = consolidatedVersionBumpBuildTriggers,
                IsDefaultVcsRootUsed = false,
                SourceDependencies = consolidatedVersionBumpSourceDependencies.ToArray(),
                IsSshAgentRequired = true,
                Parameters = consolidatedVersionBumpParameters.ToArray()
            } );

        bool TryAddPreOrPostDeploymentBuildConfiguration(
            string objectName,
            string name,
            string command,
            string commandName,
            Func<DependencyDefinition, string?> getBranch )
        {
            List<TeamCitySourceDependency> sourceDependencies = new();
            List<TeamCitySnapshotDependency> snapshotDependencies = new();
            List<TeamCityBuildStep> steps = new();
            List<TeamCityBuildConfigurationParameterBase> parameters = new();

            if ( product.MainVersionDependency == null )
            {
                context.Console.WriteError( "Main version dependency is not set for the consolidated project." );

                return false;
            }

            foreach ( var project in subprojects )
            {
                if ( project.Id == consolidatedProjectId.Id || project.Id == $"{consolidatedProjectId.Id}_NuGet" )
                {
                    continue;
                }

                if ( !product.ProductFamily.TryGetDependencyDefinitionByCiId( project.Id, out var projectDependencyDefinition ) )
                {
                    // This is a container for other projects.
                    continue;
                }

                sourceDependencies.Add( CreateSourceDependencyFromDefinition( projectDependencyDefinition ) );

                if ( projectDependencyDefinition.VcsRepository.DefaultBranchParameter != VcsRepository.DefaultDefaultBranchParameter )
                {
                    var dependencyBranch = getBranch( projectDependencyDefinition );

                    if ( dependencyBranch == null )
                    {
                        context.Console.WriteError( $"The '{projectDependencyDefinition.Name}' doesn't have the required branch set for {command}ing." );

                        return false;
                    }

                    parameters.Add(
                        new TeamCityTextBuildConfigurationParameterBase(
                            projectDependencyDefinition.VcsRepository.DefaultBranchParameter,
                            projectDependencyDefinition.VcsRepository.DefaultBranchParameter,
                            $"Default branch of {project.Name}",
                            dependencyBranch ) );
                }

                var projectRelativeId = project.Id.Split( '_' ).Last();

                steps.Add(
                    new TeamCityEngineeringCommandBuildStep(
                        $"{objectName}_{projectRelativeId}",
                        $"{commandName} deployment of {project.Name}",
                        command,
                        "--configuration Public --buildNumber %build.number% --buildType %system.teamcity.buildType.id% --use-local-dependencies",
                        areCustomArgumentsAllowed: true ) { WorkingDirectory = $"source-dependencies/{project.Name}" } );

                // Dependencies outside of the product family are fetched from the artifacts.
                if ( buildConfigurationsById.TryGetValue( $"{project.Id}_{publicBuildObjectName}", out var publicBuildConfiguration ) )
                {
                    // If not found, the project is not published.

                    foreach ( var dependencyConfigurationId in publicBuildConfiguration.SnapshotDependencies )
                    {
                        var dependencyProjectId = string.Join( '_', dependencyConfigurationId.Split( '_' ).SkipLast( 1 ) );

                        if ( !product.ProductFamily.TryGetDependencyDefinitionByCiId( dependencyProjectId, out var dependencyDefinition ) )
                        {
                            context.Console.WriteError(
                                $"Dependency definition for project '{dependencyProjectId}' (configuration '{dependencyConfigurationId}') not found." );

                            return false;
                        }

                        if ( dependencyDefinition.ProductFamily != projectDependencyDefinition.ProductFamily )
                        {
                            var dependencyName = dependencyDefinition.Name;
                            var configuration = BuildConfiguration.Public;
                            var dependencyMsBuildConfiguration = dependencyDefinition.MSBuildConfiguration[configuration];
                            var dependencyBuildInfo = new BuildInfo( null, configuration.ToString(), dependencyMsBuildConfiguration, null );

                            var dependencyPrivateArtifactsDirectory = dependencyDefinition.PrivateArtifactsDirectory.ToString( dependencyBuildInfo )
                                .Replace( Path.DirectorySeparatorChar, '/' );

                            var artifactRules =
                                $"+:{dependencyPrivateArtifactsDirectory}/{dependencyName}.version.props=>source-dependencies/{project.Name}/dependencies/{dependencyName}";

                            snapshotDependencies.Add( new TeamCitySnapshotDependency( dependencyConfigurationId, true, artifactRules ) );
                        }
                    }
                }
            }

            sourceDependencies.Add( CreateSourceDependencyFromDefinition( product.DependencyDefinition ) );

            steps.Add(
                new TeamCityEngineeringCommandBuildStep(
                    $"{objectName}_{consolidatedProjectId.Id}",
                    $"{commandName} consolidated deployment",
                    command,
                    "--configuration Public --buildNumber %build.number% --buildType %system.teamcity.buildType.id% --use-local-dependencies" )
                {
                    WorkingDirectory = $"source-dependencies/{consolidatedProjectName}"
                } );

            var branch = getBranch( product.DependencyDefinition );

            if ( branch == null )
            {
                context.Console.WriteError( $"The consolidated project doesn't have the required branch set for {command}ing." );

                return false;
            }

            tcConfigurations.Add(
                new TeamCityBuildConfiguration(
                    objectName,
                    name,
                    branch,
                    defaultBranchParameter,
                    vcsRootId,
                    product.ResolvedBuildAgentRequirements )
                {
                    BuildSteps = steps.ToArray(),
                    SourceDependencies = sourceDependencies.ToArray(),
                    SnapshotDependencies = snapshotDependencies.ToArray(),
                    IsDefaultVcsRootUsed = false,
                    IsSshAgentRequired = true,
                    Parameters = parameters.ToArray()
                } );

            return true;
        }

        // Pre-deployment
        const string preDeploymentObjectName = "PreDeployment";
        const string preDeploymentName = "2. Prepare Deployment [Public]";

        if ( !TryAddPreOrPostDeploymentBuildConfiguration( preDeploymentObjectName, preDeploymentName, "prepublish", "Prepare", d => d.Branch ) )
        {
            return false;
        }

        tcConfigurations.Add( consolidatedPublicBuildConfiguration );
        nuGetConfigurations.Add( nuGetPublicBuildConfiguration );

        // Public deployment
        const string publicDeploymentObjectName = "PublicDeployment";
        const string publicDeploymentName = "Deploy [Public]";
        var publicConsolidatedBuildCiId = $"{consolidatedProjectIdPrefix}{publicBuildObjectName}";
        var publicNuGetBuildCiId = $"{consolidatedProjectIdPrefix}{MarkNuGetObjectId( publicBuildObjectName )}";
        var publicNuGetDeploymentCiId = $"{consolidatedProjectIdPrefix}{MarkNuGetObjectId( publicDeploymentObjectName )}";

        var nuGetPublicDeploymentSteps = new TeamCityBuildStep[] { new TeamCityEngineeringPublishBuildStep( publicConfiguration ) };

        // TODO: Only Public builds of dependencies that define version need to be included.
        //       Here we include all Public builds which will cause download of all artifacts.
        var nuGetPublicDeploymentDependencies =
            nuGetPublicBuildDependencies
                .Select( d => new TeamCitySnapshotDependency(
                             d.ObjectId.Replace( $"_{publicBuildObjectName}", $"_{publicDeploymentObjectName}", StringComparison.Ordinal ),
                             true ) )
                .Concat( nuGetPublicBuildDependencies )
                .Append( new TeamCitySnapshotDependency( publicNuGetBuildCiId, true, nuGetBuildArtifactRules ) );

        nuGetConfigurations.Add(
            new TeamCityBuildConfiguration(
                MarkNuGetObjectId( publicDeploymentObjectName ),
                publicDeploymentName,
                deploymentBranch,
                defaultBranchParameter,
                vcsRootId,
                product.ResolvedBuildAgentRequirements )
            {
                BuildSteps = nuGetPublicDeploymentSteps,
                SnapshotDependencies = nuGetPublicDeploymentDependencies.ToArray(),
                BuildTimeOutThreshold = product.DeploymentTimeOutThreshold,
                IsDeployment = true
            } );

        var publicDeploymentBuildConfigurations = buildConfigurationsByKind[publicDeploymentObjectName];
        var publicDeploymentBuildConfigurationIds = publicDeploymentBuildConfigurations.Select( c => c.BuildConfigurationId ).ToArray();

        // Include dependants of the public deployment build configurations, like search update.
        var publicDeploymentDependants = buildConfigurations.Where( c => c.SnapshotDependencies.Intersect( publicDeploymentBuildConfigurationIds ).Any() )
            .Select( c => c.BuildConfigurationId )
            .Where( c => !c.StartsWith( consolidatedProjectIdPrefix, StringComparison.Ordinal ) )
            .Except( publicDeploymentBuildConfigurationIds )
            .ToArray();

        var consolidatedPublicDeploymentSnapshotDependencies =
            publicDeploymentBuildConfigurationIds
                .Concat( publicDeploymentDependants )
                .Select( c => new TeamCitySnapshotDependency( c, true ) )
                .Append( new TeamCitySnapshotDependency( publicConsolidatedBuildCiId, true ) )
                .Append( new TeamCitySnapshotDependency( publicNuGetDeploymentCiId, true ) );

        tcConfigurations.Add(
            new TeamCityBuildConfiguration(
                publicDeploymentObjectName,
                $"4. {publicDeploymentName}",

                // We should use deploymentBranch here, but TeamCity doesn't support parameterized branches in snapshot dependencies.
                defaultBranch,
                defaultBranchParameter,
                vcsRootId,
                product.ResolvedBuildAgentRequirements )
            {
                SnapshotDependencies = consolidatedPublicDeploymentSnapshotDependencies.ToArray(), IsDeployment = true
            } );

        // Post-deployment
        const string postDeploymentObjectName = "PostDeployment";
        const string postDeploymentName = "5. Finish Deployment [Public]";

        if ( !TryAddPreOrPostDeploymentBuildConfiguration( postDeploymentObjectName, postDeploymentName, "postpublish", "Finish", d => d.ReleaseBranch ) )
        {
            return false;
        }

        // Add NuGet ZIP project
        var nuGetProject = new TeamCityProject( "NuGet", "NuGet", nuGetConfigurations.ToArray() );

        var tcProject = new TeamCityProject( tcConfigurations.ToArray(), [nuGetProject] );

        GeneratePom( context, consolidatedProjectId.Id, product.DependencyDefinition.CiConfiguration.BaseUrl );
        GenerateTeamCityConfiguration( context, tcProject );

        return true;
    }
}