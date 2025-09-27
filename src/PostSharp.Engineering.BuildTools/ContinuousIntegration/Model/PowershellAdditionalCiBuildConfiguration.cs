// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

[PublicAPI]
public class PowershellAdditionalCiBuildConfiguration : AdditionalCiBuildConfiguration
{
    public PowershellAdditionalCiBuildConfiguration( string id, string name, string branch, string script, string arguments ) : base( id, name, branch )
    {
        this.Script = script;
        this.Arguments = arguments;
    }

    public string Script { get; }

    public string Arguments { get; }

    internal override TeamCityBuildConfiguration TeamCityBuildConfiguration( ProductProperties productProperties )
    {
        var product = productProperties.Product;

        var downstreamMergeConfiguration = new TeamCityBuildConfiguration(
            this.Id,
            this.Name,
            this.Branch,
            productProperties.VcsId,
            product.ResolvedBuildAgentRequirements )
        {
            BuildSteps =
            [
                new PowerShellBuildStep(
                    "Exec",
                    $"Execute {this.Script}",
                    this.Script,
                    this.Arguments,
                    product.DockerSpec )
            ],
            IsSshAgentRequired = productProperties.IsRepoRemoteSsh,
            SourceDependencies = this.AddSourceDependencies ? productProperties.SourceDependencies : []
        };

        return downstreamMergeConfiguration;
    }
}