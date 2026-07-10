// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using System;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

[PublicAPI]
public partial class MetalamaDependencies
{
    private const string _projectName = "Metalama";

    /// <summary>
    /// Returns the TeamCity GitHub App connection that serves the given organization, or <c>null</c> for a repository
    /// that is not hosted on GitHub. A connection can only issue tokens for the repositories of its own organization,
    /// and the Metalama families contain repositories of both organizations.
    /// </summary>
    internal static string? GetGitHubAppConnectionId( MetalamaGitHubOrganization? organization )
        => organization switch
        {
            MetalamaGitHubOrganization.PostSharp => GitHubAppConnections.PostSharp,
            MetalamaGitHubOrganization.Metalama => GitHubAppConnections.Metalama,
            _ => null
        };

    internal static VcsRepository CreateMetalamaVcsRepository(
        string name,
        VcsProvider provider,
        MetalamaGitHubOrganization? organization,
        string? defaultBranchParameter )
    {
        switch ( provider )
        {
            case VcsProvider.AzureDevOps:
                if ( organization != null )
                {
                    throw new InvalidOperationException( "Azure DevOps does not support organizations." );
                }

                return new AzureDevOpsRepository( _projectName, name, defaultBranchParameter: defaultBranchParameter );

            case VcsProvider.GitHub:
                if ( organization == null )
                {
                    throw new InvalidOperationException( "GitHub requires an organization." );
                }

                var organizationName = organization switch
                {
                    MetalamaGitHubOrganization.PostSharp => "postsharp",
                    MetalamaGitHubOrganization.Metalama => "metalama",
                    _ => throw new InvalidOperationException( $"Unknown GitHub organization: \"{organization}\"" )
                };

                return new GitHubRepository( name, owner: organizationName, defaultBranchParameter: defaultBranchParameter );

            default:
                throw new InvalidOperationException( $"Unknown VCS provider: \"{provider}\"" );
        }
    }
}