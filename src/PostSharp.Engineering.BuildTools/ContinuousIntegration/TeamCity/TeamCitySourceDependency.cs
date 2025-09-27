// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;

internal record TeamCitySourceDependency( string ObjectId, bool IsAbsoluteId, string ArtifactRules );