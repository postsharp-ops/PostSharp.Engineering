// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools;

public static class EnvironmentVariableNames
{
    // Our infrastructure
    public const string IsPostSharpOwned = "IS_POSTSHARP_OWNED";
    public const string IsTeamCityAgent = "IS_TEAMCITY_AGENT";
    public const string EngUserName = "ENG_USERNAME";
    public const string SignServerSecret = "SIGNSERVER_SECRET";
    public const string DocInvalidationKey = "DOC_API_KEY";
    public const string DownloadsInvalidationKey = "DOWNLOADS_API_KEY";

    // AWS
    public const string AwsAccessKeyId = "AWS_ACCESS_KEY_ID";
    public const string AwsAccessKeySecret = "AWS_SECRET_ACCESS_KEY";

    // TeamCity
    public const string TeamCityToken = "TEAMCITY_CLOUD_TOKEN";

    // NuGet.org
    public const string NuGetOrgApiKey = "NUGET_ORG_API_KEY";

    // GitHub
    public const string GitHubToken = "GITHUB_TOKEN";
    public const string GitHubReviewerToken = "GITHUB_REVIEWER_TOKEN";
    public const string GitHubAuthorEmail = "GITHUB_AUTHOR_EMAIL";

    // VS Marketplace
    public const string VsMarketplaceAccessToken = "VS_MARKETPLACE_ACCESS_TOKEN";

    // Azure DevOps Feeds
    public const string AzEndpoints = "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS";

    // TypeSense
    public const string TypeSenseApiKey = "TYPESENSE_API_KEY";

    // Azure
    public const string AzIdentityUserName = "AZ_IDENTITY_USERNAME";
    public const string AzureDevOpsUser = "AZURE_DEVOPS_USER";
    public const string AzureDevOpsToken = "AZURE_DEVOPS_TOKEN";
    public const string AzureClientId = "AZURE_CLIENT_ID";
    public const string AzureClientSecret = "AZURE_CLIENT_SECRET";
    public const string AzureTenantId = "AZURE_TENANT_ID";

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
        NuGetOrgApiKey,
        AwsAccessKeyId,
        AwsAccessKeySecret,
        TypeSenseApiKey,
        AzIdentityUserName,
        AzureClientId,
        AzureClientSecret,
        AzureTenantId,
        DocInvalidationKey,
        DownloadsInvalidationKey
    ];
}