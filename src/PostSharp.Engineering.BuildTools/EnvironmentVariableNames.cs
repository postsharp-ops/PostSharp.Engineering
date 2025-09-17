// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools;

public static class EnvironmentVariableNames
{
    public const string TeamCityOnPremToken = "TEAMCITY_TOKEN";
    public const string TeamCityCloudToken = "TEAMCITY_CLOUD_TOKEN";
    public const string VsMarketplaceAccessToken = "VS_MARKETPLACE_ACCESS_TOKEN";
    public const string GitHubToken = "GITHUB_TOKEN";
    public const string IsPostSharpOwned = "IS_POSTSHARP_OWNED";
    public const string IsTeamCityAgent = "IS_TEAMCITY_AGENT";
    public const string EngUserName = "ENG_USERNAME";
    public const string SignServerSecret = "SIGNSERVER_SECRET";
    public const string AzEndpoints = "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS";
    public const string AzureDevOpsUser = "AZURE_DEVOPS_USER";
    public const string AzureDevOpsToken = "AZURE_DEVOPS_TOKEN";
    public const string GitHubReviewerToken = "GITHUB_REVIEWER_TOKEN";
    public const string GitHubAuthorEmail = "GITHUB_AUTHOR_EMAIL";
    public const string NuGetOrgApiKey = "NUGET_ORG_API_KEY";
    public const string AwsAccessKeyId = "AWS_ACCESS_KEY_ID";
    public const string AwsAccessKeySecret = "AWS_SECRET_ACCESS_KEY";
    public const string TypeSenseApiKey = "TYPESENSE_API_KEY";
    public const string AzIdentityUserName = "AZ_IDENTITY_USERNAME";

    public static readonly string[] All =
    [
        TeamCityOnPremToken,
        TeamCityCloudToken,
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
        AzIdentityUserName
    ];

}