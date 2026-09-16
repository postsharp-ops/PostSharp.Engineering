// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

[PublicAPI]
public static partial class BackstageDependencies
{
    // "Backstage" is the short name of the product inside PostSharp.Engineering and on TeamCity. The repository is
    // named "SharpCrafters.Backstage". The neutral packages are "SharpCrafters.Backstage*" and "SharpCrafters.Common", and the
    // product customizations are "Metalama.Backstage*" and, later, "PostSharp.Backstage*".
    private const string _projectName = "Backstage";
}
