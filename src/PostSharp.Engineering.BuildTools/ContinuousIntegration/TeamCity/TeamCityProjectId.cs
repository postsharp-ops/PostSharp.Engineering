// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;

public class TeamCityProjectId
{
    public string Id { get; }

    public string ParentId { get; }

    public TeamCityProjectId( string id, string parentId )
    {
        this.Id = id;
        this.ParentId = parentId;
    }

    public override string ToString() => this.Id;
}