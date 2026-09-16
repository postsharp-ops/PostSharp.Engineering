// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

[PublicAPI]
public static partial class FoundationsDependencies
{
    // "Foundations" is the short name of the product inside PostSharp.Engineering and on TeamCity. The repository and
    // the NuGet packages are named "SharpCrafters.Foundations".
    private const string _projectName = "Foundations";
}
