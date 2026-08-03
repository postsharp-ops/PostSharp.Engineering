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
using System.Threading;
using System.Xml.Linq;

namespace PostSharp.Engineering.BuildTools.Tools.TeamCity
{
    public class TeamCityClient : IDisposable
    {
        /// <summary>
        /// Bounds a single REST call, i.e. every request issued through <see cref="TryGet(string,ConsoleHelper?,out
        /// HttpResponseMessage,bool)"/> and <see cref="TryPost"/>. It is a last-resort guard against a call that
        /// never answers, and is deliberately far longer than any healthy call needs, because a TeamCity instance
        /// under load can take minutes to respond and failing early is worse than waiting.
        /// </summary>
        /// <remarks>
        /// This bounds the total duration of a call, which is the right shape for a request whose response is a
        /// small buffered document -- including the calls that enumerate the artifact tree in
        /// <see cref="TryDownloadArtifacts"/>. It is the wrong shape for streaming an artifact body, so that is
        /// bounded by the idle timeout in <see cref="FileDownloader"/> instead.
        /// </remarks>
        private static readonly TimeSpan _requestTimeout = TimeSpan.FromMinutes( 10 );

        private readonly HttpClient _httpClient;

        /// <param name="traceConsole">When not <c>null</c>, every request and response is traced to this console.
        /// Only verbose mode passes it, because the download progress bar would overwrite the trace.</param>
        public TeamCityClient( string baseAddress, string token, ConsoleHelper? traceConsole = null )
        {
            HttpMessageHandler handler = new HttpClientHandler();

            if ( traceConsole != null )
            {
                handler = new HttpTraceHandler( traceConsole, handler );
            }

            this._httpClient = new HttpClient( handler );
            this._httpClient.BaseAddress = new Uri( baseAddress );
            this._httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", token );

            // HttpClient.Timeout bounds a request only up to the response headers: reads from the response body
            // stream are not covered by it at all (verified on .NET 8 -- a body that goes silent after the headers
            // arrive hangs indefinitely rather than timing out). On this client, which also streams artifacts,
            // that is the worst of both worlds. It fails a request whose headers are slow to arrive over a
            // saturated link, while giving a stalled transfer no protection whatsoever.
            // FileDownloader applies an idle timeout that does cover the body; the REST calls below bound
            // themselves per request, which is where a total-duration limit is actually the right shape.
            this._httpClient.Timeout = Timeout.InfiniteTimeSpan;
        }

        /// <summary>
        /// Bounds one REST call while still honoring Ctrl-C. A response obtained with this token must be fully
        /// buffered before the source is disposed, which holds for the calls below: they use the default
        /// <see cref="HttpCompletionOption.ResponseContentRead"/>, so the body has already been read by the time
        /// the call returns.
        /// </summary>
        private static CancellationTokenSource CreateRequestTimeoutSource()
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource( ConsoleHelper.CancellationToken );
            source.CancelAfter( _requestTimeout );

            return source;
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
            using var timeout = CreateRequestTimeoutSource();
            response = this._httpClient.GetAsync( path, timeout.Token ).ConfigureAwait( false ).GetAwaiter().GetResult();

            if ( writeError )
            {
                ReportHttpErrorIfAny( response, console );
            }

            if ( !response.IsSuccessStatusCode )
            {
                console?.WriteWarning( $"HTTP GET {path}  failed with code {response.StatusCode}." );
            }

            return response.IsSuccessStatusCode;
        }

        private bool TryGet( string path, out HttpResponseMessage response ) => this.TryGet( path, null, out response, false );

        private bool TryPost( string path, string payload, ConsoleHelper console, out HttpResponseMessage response )
        {
            var content = new StringContent( payload, Encoding.UTF8, "application/xml" );

            using var timeout = CreateRequestTimeoutSource();
            response = this._httpClient.PostAsync( path, content, timeout.Token ).ConfigureAwait( false ).GetAwaiter().GetResult();

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

        /// <summary>
        /// Gets the last successful build of a branch, whose name the build server may record either bare
        /// (<c>release/2026.0</c>) or fully qualified (<c>refs/heads/release/2026.0</c>).
        /// </summary>
        /// <remarks>
        /// Both spellings are queried and the newer build wins. Trying them in order and taking the first that
        /// answers is not enough: reconfiguring a VCS root changes the spelling the server records from that point
        /// on, which splits the history of a single branch between the two names, each locator returning only its
        /// own side and neither of them empty. First-hit-wins then pins the branch to whichever side happens to be
        /// tried first -- which is how a reconfigured branch kept resolving to the last build made *before* the
        /// reconfiguration, long after newer ones existed, and how `dependencies update` stopped being able to move
        /// off it: the artifacts of that stale build had meanwhile been cleaned up, so every download of it failed.
        /// </remarks>
        public bool TryGetLatestBuildId( ConsoleHelper console, string buildTypeId, string branchName, out CiBuildId? buildId )
        {
            const string prefix = "refs/heads/";

            var nakedBranchName = branchName.StartsWith( prefix, StringComparison.Ordinal )
                ? branchName.Substring( prefix.Length )
                : branchName;

            var foundNaked = this.TryGetLatestBuildIdCore( console, buildTypeId, nakedBranchName, out var nakedBuildId, out var nakedInternalId );

            var foundPrefixed = this.TryGetLatestBuildIdCore(
                console,
                buildTypeId,
                prefix + nakedBranchName,
                out var prefixedBuildId,
                out var prefixedInternalId );

            if ( !foundNaked && !foundPrefixed )
            {
                console.WriteError( $"Cannot get the last build for build type '{buildTypeId}', branch '{branchName}': No build available." );
                buildId = null;

                return false;
            }

            // Compared on the internal identifier rather than the build number, because the number is only
            // guaranteed to increase within one build configuration when its format is left alone, while the
            // identifier always does.
            var nakedWins = foundNaked && (!foundPrefixed || nakedInternalId > prefixedInternalId);

            buildId = nakedWins ? nakedBuildId : prefixedBuildId;

            return true;
        }

        private bool TryGetLatestBuildIdCore(
            ConsoleHelper console,
            string buildTypeId,
            string branchName,
            out CiBuildId? buildId,
            out long internalId )
        {
            internalId = -1;
            var path = $"/app/rest/builds?locator=defaultFilter:false,state:finished,status:SUCCESS,buildType:{buildTypeId},branch:{branchName}";

            if ( !this.TryGet( path, console, out var response ) )
            {
                console.WriteError( $"Cannot get the last build for build type '{buildTypeId}', branch '{branchName}': HTTP GET failed." );

                buildId = null;

                return false;
            }

            var document = response.Content.ReadAsXDocument();
            var build = document.Root?.Elements( "build" ).FirstOrDefault();

            if ( build == null )
            {
                buildId = null;

                return false;
            }
            else
            {
                buildId = new CiBuildId( int.Parse( build.Attribute( "number" )!.Value, CultureInfo.InvariantCulture ), buildTypeId );
                internalId = long.Parse( build.Attribute( "id" )!.Value, CultureInfo.InvariantCulture );

                return true;
            }
        }

        public bool TryDownloadArtifacts(
            ConsoleHelper console,
            string buildTypeId,
            int buildNumber,
            string artifactsPath,
            string restoreDirectory,
            bool showProgress,
            bool verbose = false )
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

            var baseTargetDirectory = Path.Combine( restoreDirectory, artifactsPath.Replace( '/', Path.DirectorySeparatorChar ) );

            var files = GetFiles( basePath, baseTargetDirectory );

            var success = FileDownloader.DownloadAsync( files, this._httpClient, console, showProgress, verbose: verbose )
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