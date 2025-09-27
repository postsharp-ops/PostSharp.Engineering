// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

public abstract class AdditionalCiBuildConfiguration
{
    public string Name { get; }

    public string Id { get; }

    public string Branch { get; }

    public bool AddSourceDependencies { get; init; }

    protected AdditionalCiBuildConfiguration( string id, string name, string branch )
    {
        this.Id = id;
        this.Name = name;
        this.Branch = branch;
    }

    internal abstract TeamCityBuildConfiguration TeamCityBuildConfiguration( ProductProperties productProperties );
}