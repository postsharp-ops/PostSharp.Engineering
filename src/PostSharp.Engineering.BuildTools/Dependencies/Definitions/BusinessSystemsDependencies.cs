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
        public BusinessSystemsDependencyDefinition( string dependencyName, bool isGitHub = false )
            : base(
                Family,
                dependencyName,
                "master",
                null,
                isGitHub ? new GitHubRepository( dependencyName, "postsharp" ) : new AzureDevOpsRepository( Family.Name, dependencyName ),
                TeamCityHelper.CreateConfiguration( TeamCityHelper.GetProjectId( dependencyName, "Websites And Business Systems" ) ),
                false )
        {
            this.Dependencies = [DevelopmentDependencies.PostSharpEngineering];

            // Most repositories of this family are hosted on Azure DevOps, so the family has no GitHub App connection.
            this.GitHubAppConnectionId = isGitHub ? GitHubAppConnections.PostSharp : null;
        }
    }

    public static ProductFamily Family { get; } = new( "Business%20Systems", "1.0", DevelopmentDependencies.Family );

    public static DependencyDefinition BusinessSystems { get; } = new BusinessSystemsDependencyDefinition( "BusinessSystems" );

    public static DependencyDefinition HelpBrowser { get; } = new BusinessSystemsDependencyDefinition( "HelpBrowser" );

    public static DependencyDefinition PostSharpWeb { get; } = new BusinessSystemsDependencyDefinition( "PostSharpWeb" );

    public static DependencyDefinition MetalamaMarketplace { get; } = new BusinessSystemsDependencyDefinition( "MetalamaMarketplace", true );
}