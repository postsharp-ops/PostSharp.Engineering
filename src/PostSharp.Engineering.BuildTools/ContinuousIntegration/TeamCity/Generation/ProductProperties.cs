// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;
using System;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;

internal class ProductProperties
{
    public Product Product { get; }

    public string DeploymentBranch => this.Product.DependencyDefinition.PublishingBranch;

    public string Branch => this.Product.DependencyDefinition.Branch;

    public string DefaultBranch => this.Product.DependencyDefinition.Branch;

    public bool IsRepoRemoteSsh => this.Product.DependencyDefinition.VcsRepository.IsSshAgentRequired;

    public string VcsId => TeamCityHelper.GetVcsId( this.Product.DependencyDefinition );

    public string PublicArtifactsDirectory { get; }

    public string TestResultsDirectory { get; }

    public string LogsDirectory { get; }

    public string DumpsDirectory { get; }

    public TeamCitySourceDependency[] SourceDependencies { get; }

    public TeamCitySourceDependency[] EngOnlySourceDependencies { get; }

    public ProductProperties( Product product )
    {
        this.Product = product;

        // Calculate product-level artifact directories
        this.PublicArtifactsDirectory = product.PublicArtifactsDirectory.Replace( "\\", "/", StringComparison.Ordinal );
        this.TestResultsDirectory = product.TestResultsDirectory.Replace( "\\", "/", StringComparison.Ordinal );
        this.LogsDirectory = product.LogsDirectory.Replace( "\\", "/", StringComparison.Ordinal );
        this.DumpsDirectory = product.DumpDirectory.Replace( "\\", "/", StringComparison.Ordinal );

        this.SourceDependencies = product.SourceDependencies.Select( d => new TeamCitySourceDependency(
                                                                         d.CiConfiguration.ProjectId.ToString(),
                                                                         TeamCityHelper.GetVcsId( d ),
                                                                         true,
                                                                         $"+:. => {product.SourceDependenciesDirectory}/{d.Name}" ) )
            .ToArray();

        this.EngOnlySourceDependencies = product.SourceDependencies.Select( d =>
            {
                var checkoutRules = $"""
                                     +:{d.EngineeringDirectory} => {product.SourceDependenciesDirectory}/{d.Name}/{d.EngineeringDirectory}
                                     +:DockerBuild.ps1 => {product.SourceDependenciesDirectory}/{d.Name}/DockerBuild.ps1
                                     +:Build.ps1 => {product.SourceDependenciesDirectory}/{d.Name}/Build.ps1
                                     """ +
                                    Environment.NewLine +
                                    string.Join(
                                        Environment.NewLine,
                                        d.AdditionalEngineeringDirectories.Select( x => $"+{x} => {product.SourceDependenciesDirectory}/{d.Name}/{x}" ) );

                return new TeamCitySourceDependency(
                    d.CiConfiguration.ProjectId.ToString(),
                    TeamCityHelper.GetVcsId( d ),
                    true,
                    checkoutRules );
            } )
            .ToArray();
    }
}