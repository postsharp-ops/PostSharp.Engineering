// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System.Diagnostics.CodeAnalysis;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

[PublicAPI]
public abstract class VcsRepository( string? defaultBranchParameter )
{
    public const string DefaultDefaultBranchParameter = "DefaultBranch";

    public abstract string Name { get; }
    
    /// <summary>
    /// Parameter name used to specify the default branch in TeamCity build configurations.
    /// </summary>
    public string DefaultBranchParameter { get; } = defaultBranchParameter ?? DefaultDefaultBranchParameter;

    public abstract VcsProvider Provider { get; }
    
    public abstract string SshUrl { get; }

    public abstract string HttpUrl { get; }
    
    /// <summary>
    /// Gets the repository URL used by developers on their machines to access the repository.
    /// </summary>
    public abstract string DeveloperMachineRemoteUrl { get; }
    
    /// <summary>
    /// Gets the repository URL used by TeamCity server and agents to access the repository.
    /// </summary>
    public abstract string TeamCityRemoteUrl { get; }
    
    public abstract bool IsSshAgentRequired { get; }
    
    public abstract bool TryDownloadTextFile( ConsoleHelper console, string branch, string path, [NotNullWhen( true )] out string? text );

    /// <summary>
    /// Returns the URL that identifies the repository and allows user to access the repository using a web browser.
    /// </summary>
    /// <remarks>No matter which data is contained in the resulting string, the use of the result should be for UI and debugging only.</remarks>
    /// <returns>A string identifying the repository.</returns>
    public override string ToString() => this.HttpUrl;
}