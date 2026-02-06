// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public record AdditionalDockerfile( string Name, ContainerComponent[] Components );