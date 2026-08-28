// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public record AdditionalDockerfile( string Name, ContainerComponent[] Components )
{
    /// <summary>
    /// Gets the requirements this chain is generated from. When null, the product's own
    /// <see cref="Build.Model.Product.OverriddenBuildAgentRequirements"/> are used and <see cref="Components"/>
    /// are added to them. Set it to generate a chain for a different operating system or architecture, whose
    /// components are then <see cref="Components"/> plus those of the requirements given here.
    /// </summary>
    public ContainerRequirements? Requirements { get; init; }
}