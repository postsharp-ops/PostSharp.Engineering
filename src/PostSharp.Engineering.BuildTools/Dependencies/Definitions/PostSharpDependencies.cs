// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Dependencies.Definitions;

[PublicAPI]
public static partial class PostSharpDependencies
{
    private const string _projectName = "PostSharp";

    /// <summary>
    /// The TeamCity project holding every PostSharp line, and the project where the VCS roots of every line are
    /// stored. The lines beneath it do not share a build configuration. Every line follows the Metalama arrangement,
    /// where the project of the line contains one project per repository -- PostSharpGitHub_PostSharp20270_PostSharp,
    /// PostSharpGitHub_PostSharp20270_PostSharpDocumentation.
    /// </summary>
    private const string _parentProjectId = "PostSharpGitHub";
}
