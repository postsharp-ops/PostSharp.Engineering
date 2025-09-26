// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.Diagnostics.CodeAnalysis;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

public static class VcsUrlParser
{
    public static bool TryGetRepository( string url, [NotNullWhen( true )] out VcsRepository? repository )
    {
        if ( AzureDevOpsRepository.TryParse( url, out var azureDevOpsRepository ) )
        {
            repository = azureDevOpsRepository;

            return true;
        }
        else if ( GitHubRepository.TryParse( url, out var gitHubRepository ) )
        {
            repository = gitHubRepository;

            return true;
        }
        else
        {
            repository = null;

            return false;
        }
    }
}