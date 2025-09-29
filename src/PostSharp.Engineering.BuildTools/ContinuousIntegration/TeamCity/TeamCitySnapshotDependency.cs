// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;

internal record TeamCitySnapshotDependency(
    string ObjectId,
    bool IsAbsoluteId,
    ArtifactRule[]? ArtifactRules = null,
    FailureAction FailureAction = FailureAction.FailToStart );

internal enum FailureAction
{
    FailToStart,
    AddProblem,
    Ignore,
    Cancel
}