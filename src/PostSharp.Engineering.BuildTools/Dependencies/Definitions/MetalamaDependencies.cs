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

    private static VcsRepository CreateMetalamaVcsRepository( string name, VcsProvider provider, MetalamaGitHubOrganization? organization, string? defaultBranchParameter )
    {
        switch ( provider )
        {
            case VcsProvider.AzureDevOps:
                if (organization != null )
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