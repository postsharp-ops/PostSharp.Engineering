// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Tools.TeamCity;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;

/// <param name="Definition">Definition of the dependency this VCS root checks out. It tells which repository the build
/// reaches, which is what the build-scoped token must be scoped to.</param>
/// <param name="CheckoutRules">Rules mapping the repository of the dependency into the working directory of the build.</param>
internal record TeamCitySourceDependency( DependencyDefinition Definition, string CheckoutRules )
{
    /// <summary>
    /// Gets the identifier of the VCS root of the dependency. It is always absolute, because the root is defined in the
    /// TeamCity project of the dependency, not in the one of the build.
    /// </summary>
    public string VcsId => TeamCityHelper.GetVcsId( this.Definition );
}
