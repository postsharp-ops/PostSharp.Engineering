// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;

internal record TeamCitySnapshotDependency(
    string ObjectId,
    bool IsAbsoluteId,
    string? ArtifactRules = null,
    FailureAction FailureAction = FailureAction.FailToStart,
    ReuseBuilds ReuseBuilds = ReuseBuilds.Default );

internal enum FailureAction
{
    FailToStart,
    AddProblem,
    Ignore,
    Cancel
}

internal enum ReuseBuilds
{
    Default,
    LastSuccessful
}