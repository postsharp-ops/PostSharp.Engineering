// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools;

internal static class EnvironmentVariableNames
{
    // Our infrastructure (non-secret configuration)
    public const string IsPostSharpOwned = "IS_POSTSHARP_OWNED";
    public const string IsTeamCityAgent = "IS_TEAMCITY_AGENT";
    public const string EngUserName = "ENG_USERNAME";
    public const string RepoDirectory = "ENG_REPO_DIRECTORY";

    // Our infrastructure (secrets)
    [Secret] public const string SignServerSecret = "SIGNSERVER_SECRET";
    [Secret] public const string DocInvalidationKey = "DOC_API_KEY";
    [Secret] public const string DownloadsInvalidationKey = "DOWNLOADS_API_KEY";
    [Secret] private const string _metalamaLicense = "MetalamaLicense";
    [Secret] private const string _postSharpLicense = "PostSharpLicense";

    // AWS
    [Secret] public const string AwsAccessKeyId = "AWS_ACCESS_KEY_ID";
    [Secret] public const string AwsAccessKeySecret = "AWS_SECRET_ACCESS_KEY";

    // TeamCity
    [Secret] public const string TeamCityToken = "TEAMCITY_CLOUD_TOKEN";

    // NuGet.org
    [Secret] public const string NuGetOrgApiKey = "NUGET_ORG_API_KEY";

    // Git - set by DockerBuild.ps1 from current git config.
    private const string _gitUserName = "GIT_USER_NAME";
    private const string _gitUserEmail = "GIT_USER_EMAIL";

    // GitHub
    [Secret] public const string GitHubToken = "GITHUB_TOKEN";
    [Secret] public const string GitHubReviewerToken = "GITHUB_REVIEWER_TOKEN";
    public const string GitHubAuthorEmail = "GITHUB_AUTHOR_EMAIL";

    // VS Marketplace
    [Secret] public const string VsMarketplaceAccessToken = "VS_MARKETPLACE_ACCESS_TOKEN";

    // Azure DevOps Feeds. Used by AzureArtifactsCredentialProviderComponent.
    [Secret] public const string AzEndpoints = "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS";

    // TypeSense
    [Secret] public const string TypeSenseApiKey = "TYPESENSE_API_KEY";

    // Claude Code (not marked [Secret] because Claude CLI needs this to authenticate)
    public const string ClaudeCodeOAuthToken = "CLAUDE_CODE_OAUTH_TOKEN";

    // Azure
    public const string AzIdentityUserName = "AZ_IDENTITY_USERNAME";
    public const string AzureDevOpsUser = "AZURE_DEVOPS_USER";
    [Secret] public const string AzureDevOpsToken = "AZURE_DEVOPS_TOKEN";
    [Secret] public const string AzureClientId = "AZURE_CLIENT_ID";
    [Secret] public const string AzureClientSecret = "AZURE_CLIENT_SECRET";
    public const string AzureTenantId = "AZURE_TENANT_ID";

    // List of all environment variables, injected into DockerBuild.ps1 and passed to the container.
    public static readonly string[] All =
    [
        TeamCityToken,
        VsMarketplaceAccessToken,
        GitHubToken,
        IsPostSharpOwned,
        IsTeamCityAgent,
        EngUserName,
        SignServerSecret,
        AzEndpoints,
        AzureDevOpsUser,
        AzureDevOpsToken,
        GitHubReviewerToken,
        GitHubAuthorEmail,
        _gitUserEmail,
        _gitUserName,
        NuGetOrgApiKey,
        AwsAccessKeyId,
        AwsAccessKeySecret,
        TypeSenseApiKey,
        ClaudeCodeOAuthToken,
        AzIdentityUserName,
        AzureClientId,
        AzureClientSecret,
        AzureTenantId,
        DocInvalidationKey,
        DownloadsInvalidationKey,
        _metalamaLicense,
        _postSharpLicense
    ];
}