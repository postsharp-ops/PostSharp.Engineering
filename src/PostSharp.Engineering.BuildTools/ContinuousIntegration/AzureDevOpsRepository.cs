// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

public class AzureDevOpsRepository : VcsRepository
{
    // E.g.
    // https://postsharp@dev.azure.com/postsharp/Caravela/_git/Caravela.Repo
    // https://dev.azure.com/postsharp/Caravela/_git/Caravela.Repo
    private static readonly Regex _urlRegex = new(
        @"^(?<protocol>https)://(?:(?<user>[^/@]+)@)?(?<domain>[^/]+)/(?<organization>[^/]+)?/(?<project>[^/]+)/_git/(?<repo>[^/]+)$" );

    public override string Name { get; }

    public override VcsProvider Provider => VcsProvider.AzureDevOps;

    public string Domain { get; }

    public string Organisation { get; }

    public string Project { get; }

    public string BaseUrl => $"https://{this.Domain}/{this.Organisation}";

    public override string SshUrl => throw new NotImplementedException( "We don't use SSH for Azure DevOps at the moment." );

    public override string HttpUrl => $"{this.BaseUrl}/{this.Project}/_git/{this.Name}";

    public override string DeveloperMachineRemoteUrl => this.HttpUrl;

    public override string TeamCityRemoteUrl => this.HttpUrl;

    public override bool IsSshAgentRequired => false;

    public override string TokenEnvironmentVariableName => EnvironmentVariableNames.AzureDevOpsToken;

    public AzureDevOpsRepository(
        string project,
        string name,
        string organisation = "postsharp",
        string domain = "dev.azure.com",
        string? defaultBranchParameter = null )
    {
        this.Name = name;
        this.Domain = domain;
        this.Organisation = organisation;
        this.Project = project;
    }

    public static bool TryParse( string repoUrl, [NotNullWhen( true )] out AzureDevOpsRepository? repository )
    {
        var match = _urlRegex.Match( repoUrl );

        if ( !match.Success )
        {
            repository = null;

            return false;
        }

        var name = match.Groups["repo"].Value;
        var domain = match.Groups["domain"].Value;
        var organisation = match.Groups["organization"].Value;
        var project = match.Groups["project"].Value;
        repository = new AzureDevOpsRepository( project, name, organisation, domain );

        return true;
    }

    public override bool TryDownloadTextFile( ConsoleHelper console, string branch, string path, [NotNullWhen( true )] out string? text )
    {
        var httpClient = new HttpClient();

        var teamCitySourceReadToken = TeamCityHelper.GetTeamCitySourceReadToken();

        var authString = Convert.ToBase64String( Encoding.UTF8.GetBytes( $@"{TeamCityHelper.TeamCityUsername}:{teamCitySourceReadToken}" ) );
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Basic", authString );

        var uri = $"{this.BaseUrl}/{this.Project}/_apis/git/repositories/{this.Name}/items?path={path}&versionDescriptor.version={branch}";

        console.WriteMessage( $"Downloading {uri}." );
        text = httpClient.GetString( uri );

        return true;
    }

    public override Task<bool> TrySetBranchPoliciesAsync( BuildContext context, string buildStatusGenre, string? buildStatusName, bool dry )
        => AzureDevOpsHelper.TrySetBranchPoliciesAsync( context, this, buildStatusGenre, buildStatusName, dry );

    public override Task<string?> TryCreatePullRequestAsync( ConsoleHelper console, string sourceBranch, string targetBranch, string title )
        => AzureDevOpsHelper.TryCreatePullRequestAsync( console, this, sourceBranch, targetBranch, title );
}