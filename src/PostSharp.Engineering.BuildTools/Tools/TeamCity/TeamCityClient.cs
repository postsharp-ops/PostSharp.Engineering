// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Tools.TeamCity
{
    public class TeamCityClient : IDisposable
    {
        private readonly HttpClient _httpClient;

        public TeamCityClient( string baseAddress, string token )
        {
            this._httpClient = new HttpClient();
            this._httpClient.BaseAddress = new Uri( baseAddress );
            this._httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", token );
        }

        private static void ReportHttpErrorIfAny( HttpResponseMessage response, ConsoleHelper? console )
        {
            if ( !response.IsSuccessStatusCode )
            {
                if ( console == null )
                {
                    throw new ArgumentNullException( nameof(console) );
                }

                console.WriteError(
                    $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} failed with code {response.StatusCode}. {response.ReasonPhrase}" );

                console.WriteMessage( string.Join( Environment.NewLine, response.Content.ReadAsString().Split( '\n', '\r' ).Select( x => "> " + x ) ) );
            }
        }

        private bool TryGet( string path, ConsoleHelper? console, out HttpResponseMessage response, bool writeError = true )
        {
            response = this._httpClient.GetAsync( path, ConsoleHelper.CancellationToken ).ConfigureAwait( false ).GetAwaiter().GetResult();

            if ( writeError )
            {
                ReportHttpErrorIfAny( response, console );
            }

            return response.IsSuccessStatusCode;
        }

        private bool TryGet( string path, out HttpResponseMessage response ) => this.TryGet( path, null, out response, false );

        private bool TryPost( string path, string payload, ConsoleHelper console, out HttpResponseMessage response )
        {
            var content = new StringContent( payload, Encoding.UTF8, "application/xml" );
            response = this._httpClient.PostAsync( path, content, ConsoleHelper.CancellationToken ).ConfigureAwait( false ).GetAwaiter().GetResult();

            ReportHttpErrorIfAny( response, console );

            return response.IsSuccessStatusCode;
        }

        public bool TryGetBranchFromBuildNumber( ConsoleHelper console, CiBuildId buildId, [NotNullWhen( true )] out string? branch )
        {
            var path =
                $"/app/rest/builds?locator=defaultFilter:false,state:finished,status:SUCCESS,buildType:{buildId.BuildTypeId},number:{buildId.BuildNumber}";

            if ( !this.TryGet( path, console, out var response ) )
            {
                branch = null;

                return false;
            }

            var document = response.Content.ReadAsXDocument();
            var build = document.Root?.Elements( "build" ).FirstOrDefault();

            if ( build == null )
            {
                console.WriteError( $"Cannot determine the branch of '{buildId}': cannot find any build in '{path}'." );

                branch = null;

                return false;
            }

            branch = build.Attribute( "branchName" )!.Value;

            if ( string.IsNullOrEmpty( branch ) )
            {
                console.WriteError( $"Cannot determine the branch of '{buildId}': the branch name is empty." );

                branch = null;

                return false;
            }

            return true;
        }

        public CiBuildId? GetLatestBuildId( ConsoleHelper console, string buildTypeId, string branchName )
        {
            var path = $"/app/rest/builds?locator=defaultFilter:false,state:finished,status:SUCCESS,buildType:{buildTypeId},branch:{branchName}";

            if ( !this.TryGet( path, console, out var response ) )
            {
                return null;
            }

            var document = response.Content.ReadAsXDocument();
            var build = document.Root?.Elements( "build" ).FirstOrDefault();

            if ( build == null )
            {
                return null;
            }
            else
            {
                return new CiBuildId( int.Parse( build.Attribute( "number" )!.Value, CultureInfo.InvariantCulture ), buildTypeId );
            }
        }

        public bool TryDownloadArtifacts( ConsoleHelper console, string buildTypeId, int buildNumber, string artifactsPath, string artifactsDirectory )
        {
            IEnumerable<DownloadedFile> GetFiles( string urlOrPath, string targetDirectory )
            {
                if ( !this.TryGet( urlOrPath, console, out var response ) )
                {
                    throw new InvalidOperationException( $"Failed to get '{urlOrPath}'." );
                }

                var document = response.Content.ReadAsXDocument();

                (string Name, XElement Element)[] artifacts = document.Root!.Elements( "file" )
                    .Select( f => (f.Attribute( "name" )?.Value ?? throw new InvalidOperationException( "Unknown name of an artifact." ), f) )
                    .ToArray();

                IEnumerable<(string Name, string Url, long Size)> files = artifacts
                    .Select( a => (
                                 a.Name,
                                 a.Element.Element( "content" )?.Attribute( "href" )?.Value,
                                 long.Parse( a.Element.Attribute( "size" )?.Value ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture )) )
                    .Where( a => a.Value != null )
                    .Select( a => (a.Name, a.Value!, a.Item3) );

                foreach ( var file in files )
                {
                    var targetFilePath = Path.Combine( targetDirectory, file.Name );

                    yield return new DownloadedFile( file.Url, targetFilePath, file.Name, file.Size );
                }

                IEnumerable<(string Name, string Url)> directories = artifacts
                    .Select( a => (a.Item1, a.Element.Element( "children" )?.Attribute( "href" )?.Value) )
                    .Where( a => a.Value != null )
                    .Select( a => (a.Item1, a.Value!) );

                foreach ( var directory in directories )
                {
                    var childTargetDirectory = Path.Combine( targetDirectory, directory.Name );
                    var subFiles = GetFiles( directory.Url, childTargetDirectory );

                    foreach ( var subFile in subFiles )
                    {
                        yield return subFile;
                    }
                }
            }

            var basePath =
                $"/app/rest/builds/defaultFilter:false,buildType:{buildTypeId},number:{buildNumber}/artifacts/children/{artifactsPath.Replace( '\\', '/' )}";

            var baseTargetDirectory = Path.Combine( artifactsDirectory, artifactsPath.Replace( '/', Path.DirectorySeparatorChar ) );

            var files = GetFiles( basePath, baseTargetDirectory );

            var success = FileDownloader.DownloadAsync( files, this._httpClient, console )
                .GetAwaiter()
                .GetResult();

            if ( !success )
            {
                console.WriteError( "Failed to fetch artifacts. Check the descriptions above." );
            }

            return success;
        }

        public string? ScheduleBuild( ConsoleHelper console, string buildTypeId, string comment, string? branchName = null )
        {
            var payload =
                $"<build buildTypeId=\"{buildTypeId}\"{(branchName == null ? "" : $" branchName=\"{branchName}\"")}><comment><text>{comment}</text></comment></build>";

            if ( !this.TryPost( "/app/rest/buildQueue", payload, console, out var response ) )
            {
                return null;
            }

            var document = response.Content.ReadAsXDocument();
            var build = document.Root;

            return build!.Attribute( "id" )!.Value;
        }

        public string PollRunningBuildStatus( string buildId, out string buildNumber )
        {
            var status = $"Build starting...";
            buildNumber = string.Empty;

            _ = this.TryGet( TeamCityHelper.TeamCityApiRunningBuildsPath, out var response );

            var document = response.Content.ReadAsXDocument();
            var builds = document.Root!;

            if ( !builds.Attribute( "count" )!.Value.Equals( "0", StringComparison.Ordinal ) )
            {
                var build = builds.Elements().ToArray().FirstOrDefault( e => e.Attribute( "id" )!.Value.Equals( buildId, StringComparison.Ordinal ) );

                if ( build != null && build.Attribute( "percentageComplete" ) != null )
                {
                    if ( build.Attribute( "number" ) != null )
                    {
                        buildNumber = build.Attribute( "number" )!.Value;
                    }

                    status = $"Build #{buildNumber} {build.Attribute( "state" )!.Value} ({build.Attribute( "percentageComplete" )!.Value}%)";
                }
            }

            return status;
        }

        public bool IsBuildQueued( ConsoleHelper console, string buildId )
        {
            if ( !this.TryGet( TeamCityHelper.TeamCityApiBuildQueuePath, console, out var response ) )
            {
                return false;
            }

            var document = response.Content.ReadAsXDocument();
            var builds = document.Root!;

            if ( builds.Attribute( "count" )!.Value.Equals( "0", StringComparison.Ordinal ) )
            {
                return false;
            }

            var build = builds.Elements().ToArray().FirstOrDefault( e => e.Attribute( "id" )!.Value.Equals( buildId, StringComparison.Ordinal ) );

            if ( build == null )
            {
                return false;
            }

            return true;
        }

        public bool HasBuildFinishedSuccessfully( ConsoleHelper console, string buildId )
        {
            if ( !this.TryGet( TeamCityHelper.TeamCityApiFinishedBuildsPath, console, out var response ) )
            {
                return false;
            }

            var document = response.Content.ReadAsXDocument();
            var builds = document.Root!;

            if ( builds.Attribute( "count" )!.Value.Equals( "0", StringComparison.Ordinal ) )
            {
                console.WriteError( "No finished TeamCity builds found. This might be a TeamCity API problem." );

                return false;
            }

            var build = builds.Elements().ToArray().FirstOrDefault( e => e.Attribute( "id" )!.Value.Equals( buildId, StringComparison.Ordinal ) );

            if ( build == null )
            {
                console.WriteError( $"No successfully finished TeamCity build with ID '{buildId}' found." );

                return false;
            }

            if ( !build.Attribute( "status" )!.Value.Equals( "SUCCESS", StringComparison.OrdinalIgnoreCase ) )
            {
                return false;
            }

            return true;
        }

        public bool HasBuildFinished( ConsoleHelper console, string buildId )
        {
            if ( !this.TryGet( TeamCityHelper.TeamCityApiFinishedBuildsPath, console, out var response ) )
            {
                return false;
            }

            var document = response.Content.ReadAsXDocument();
            var builds = document.Root!;

            if ( builds.Attribute( "count" )!.Value.Equals( "0", StringComparison.Ordinal ) )
            {
                console.WriteError( "No finished TeamCity builds found. This might be a TeamCity API problem." );

                return false;
            }

            var build = builds.Elements().ToArray().FirstOrDefault( e => e.Attribute( "id" )!.Value.Equals( buildId, StringComparison.Ordinal ) );

            if ( build == null )
            {
                return false;
            }

            return true;
        }

        private bool TryGetDetails( ConsoleHelper console, string path )
        {
            if ( !this.TryGet( path, console, out var response ) )
            {
                return false;
            }

            console.WriteMessage( response.Content.ReadAsXDocument().ToString() );

            return true;
        }

        public bool TryGetProjectDetails( ConsoleHelper console, string id ) => this.TryGetDetails( console, $"/app/rest/projects/id:{id}" );

        public bool TryCreateProject( ConsoleHelper console, string name, string id, string? parentId = null )
        {
            parentId ??= "_Root";

            var payload = $@"<newProjectDescription id=""{id}"" name=""{name}"">
  <parentProject locator=""id:{parentId}"" />
</newProjectDescription>";

            return this.TryPost( "/app/rest/projects", payload, console, out _ );
        }

        public bool TrySetProjectVersionedSettings( ConsoleHelper console, string projectId, string vcsRootId )
        {
            var payload = $@"<projectFeature type=""versionedSettings"">
       <properties>
        <!-- The following settings come from the project versioned settings feature. -->
        <property name=""buildSettings"" value=""PREFER_VCS"" />
        <property name=""credentialsStorageType"" value=""credentialsJSON"" />
        <property name=""enabled"" value=""true"" />
        <property name=""format"" value=""kotlin"" />
        <property name=""rootId"" value=""{vcsRootId}"" />
        <property name=""showChanges"" value=""true"" />
        <property name=""twoWaySynchronization"" value=""false"" />
        <property name=""useRelativeIds"" value=""true"" />

        <!-- The following settings come from the project versioned settings configuration. -->
        <property name=""allowUIEditing"" value=""false"" />
        <property name=""buildSettingsMode"" value=""useFromVCS"" />
        <property name=""showSettingsChanges"" value=""true"" />
        <property name=""synchronizationMode"" value=""true"" />
      </properties>
</projectFeature>";

            return this.TryPost( $"/app/rest/projects/id:{projectId}/projectFeatures", payload, console, out _ );
        }

        public bool TryGetProjectVersionedSettingsConfiguration( ConsoleHelper console, string projectId )
            => this.TryGetDetails( console, $"/app/rest/projects/id:{projectId}/versionedSettings/config" );

        public bool TryGetVcsRootDetails( ConsoleHelper console, string id ) => this.TryGetDetails( console, $"/app/rest/vcs-roots/id:{id}" );

        public bool TryGetVcsRoots( ConsoleHelper console, string projectId, [NotNullWhen( true )] out ImmutableArray<(string Id, string Url)>? vcsRoots )
        {
            int? expectedCount = null;
            vcsRoots = null;
            var vcsRootsList = new List<(string Id, string Url)>();

            var nextVcsRootsPath = $"/app/rest/vcs-roots?locator=project:(id:{projectId})";

            do
            {
                if ( !this.TryGet( nextVcsRootsPath, console, out var vcsRootsResponse ) )
                {
                    return false;
                }

                var vcsRootsElement = vcsRootsResponse.Content.ReadAsXDocument().Root!;

                var newExpectedCount = int.Parse( vcsRootsElement.Attribute( "count" )!.Value, NumberStyles.Integer, CultureInfo.InvariantCulture );

                if ( expectedCount == null )
                {
                    expectedCount = newExpectedCount;
                }
                else if ( expectedCount != newExpectedCount )
                {
                    throw new InvalidOperationException( "Inconsistent VCS roots count" );
                }

                foreach ( var partialVcsRootElement in vcsRootsElement.Elements( "vcs-root" ) )
                {
                    var vcsRootPath = partialVcsRootElement.Attribute( "href" )!.Value;

                    if ( !this.TryGet( vcsRootPath, console, out var vcsRootResponse ) )
                    {
                        return false;
                    }

                    var vcsRootElement = vcsRootResponse.Content.ReadAsXDocument().Root!;
                    var vcsRootId = vcsRootElement.Attribute( "id" )!.Value;

                    var vcsRootUrl = vcsRootElement
                        .Element( "properties" )
                        !.Elements( "property" )
                        .Single( p => p.Attribute( "name" )!.Value == "url" )
                        .Attribute( "value" )!
                        .Value;

                    vcsRootsList.Add( (vcsRootId, vcsRootUrl) );
                }

                nextVcsRootsPath = vcsRootsElement.Attribute( "nextHref" )?.Value;
            }
            while ( nextVcsRootsPath != null );

            vcsRoots = vcsRootsList.ToImmutableArray();

            if ( expectedCount == null )
            {
                throw new InvalidOperationException( "Unknown expected count." );
            }
            else if ( vcsRoots.Value.Length != expectedCount )
            {
                throw new InvalidOperationException( "Not all VCS roots have been retrieved." );
            }

            return true;
        }

        public bool TryCreateVcsRoot(
            ConsoleHelper console,
            string? projectId,
            string id,
            string name,
            string defaultBranch,
            VcsRepository repository,
            IEnumerable<string> branchSpecification )
        {
            var url = repository.TeamCityRemoteUrl;
            var properties = new List<(string Name, string Value)>();

            void AddProperty( string propertyName, string propertyValue ) => properties.Add( (propertyName, propertyValue) );

            switch ( repository )
            {
                case AzureDevOpsRepository:
                    AddProperty( "authMethod", "PASSWORD" );
                    AddProperty( "username", "teamcity@postsharp.net" );
                    AddProperty( "secure:password", "%SourceCodeWritingToken%" );
                    AddProperty( "usernameStyle", "EMAIL" );

                    break;

                case GitHubRepository:
                    AddProperty( "authMethod", "TEAMCITY_SSH_KEY" );
                    AddProperty( "teamcitySshKey", "PostSharp.Engineering" );
                    AddProperty( "usernameStyle", "USERID" );

                    break;

                default:
                    console.WriteError( $"Unknown VCS provider: {url}" );

                    return false;
            }

            AddProperty( "url", url );
            AddProperty( "agentCleanFilesPolicy", "ALL_UNTRACKED" );
            AddProperty( "agentCleanPolicy", "ALWAYS" );
            AddProperty( "ignoreKnownHosts", "true" );
            AddProperty( "submoduleCheckout", "CHECKOUT" );
            AddProperty( "useAlternates", "USE_MIRRORS" );
            AddProperty( "branch", defaultBranch );
            AddProperty( "teamcity:branchSpec", string.Join( "&#xA;", branchSpecification ) );

            var payload = $@"<vcs-root id=""{id}"" name=""{name}"" vcsName=""jetbrains.git"">
   <project id=""{projectId ?? "_Root"}""/>
   <properties>
     {string.Join( Environment.NewLine, properties.Select( p => $"<property name=\"{p.Name}\" value=\"{p.Value}\" />" ) )}
   </properties>
</vcs-root>";

            return this.TryPost( "/app/rest/vcs-roots", payload, console, out _ );
        }

        public void Dispose()
        {
            this._httpClient.Dispose();
        }
    }
}