// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

[PublicAPI]
public static class BusinessSystemsDependencies
{
    private class BusinessSystemsDependencyDefinition : DependencyDefinition
    {
        public BusinessSystemsDependencyDefinition( string dependencyName, string? gitHubOwner = null, string? gitHubAppConnectionId = null )
            : base(
                Family,
                dependencyName,
                "master",
                null,
                gitHubOwner != null
                    ? new GitHubRepository( dependencyName, gitHubOwner )
                    : new AzureDevOpsRepository( Family.Name, dependencyName ),
                TeamCityHelper.CreateConfiguration( TeamCityHelper.GetProjectId( dependencyName, "Websites And Business Systems" ) ),
                false )
        {
            this.Dependencies = [DevelopmentDependencies.PostSharpEngineering];

            // Repositories of this family are hosted on different organizations (Azure DevOps and several GitHub
            // organizations), so the family has no GitHub App connection and each GitHub repository sets its own.
            this.GitHubAppConnectionId = gitHubAppConnectionId;
        }
    }

    public static ProductFamily Family { get; } = new( "Business%20Systems", "1.0", DevelopmentDependencies.Family );

    public static DependencyDefinition BusinessSystems { get; } =
        new BusinessSystemsDependencyDefinition( "BusinessSystems", "postsharp-ops", GitHubAppConnections.PostSharpOps );

    public static DependencyDefinition HelpBrowser { get; } = new BusinessSystemsDependencyDefinition( "HelpBrowser" );

    public static DependencyDefinition MetalamaMarketplace { get; } =
        new BusinessSystemsDependencyDefinition( "MetalamaMarketplace", "postsharp", GitHubAppConnections.PostSharp );
}