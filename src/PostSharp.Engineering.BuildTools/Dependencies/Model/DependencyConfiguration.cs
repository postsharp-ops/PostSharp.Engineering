// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.BuildTools.Dependencies.Model;

public record DependencyConfiguration( DependencyDefinition Definition, BuildConfiguration Configuration );