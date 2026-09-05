// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;

internal record TeamCitySnapshotDependency(
    string ObjectId,
    bool IsAbsoluteId,
    string? ArtifactRules = null,
    FailureAction FailureAction = FailureAction.FailToStart,
    ReuseBuilds ReuseBuilds = ReuseBuilds.Default,
    string? Branch = null,

    /// <summary>
    /// Whether TeamCity empties the destination of each artifact rule before downloading. It is right when the
    /// destination is a directory of its own, and destructive when a rule unpacks into the checkout root: the
    /// clean then removes the sources, and the build fails afterwards on a missing file with no indication that
    /// the checkout was emptied.
    /// </summary>
    bool CleanDestination = true );

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