// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Testing;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

/// <summary>
/// A build configuration that runs test archives with the generated <c>RunTests.ps1</c>. <see cref="TestArchiveCells"/>
/// creates them from <see cref="Build.Model.Product.TestAgents"/> and the test applications of the product.
/// </summary>
/// <remarks>
/// It downloads the archives that its snapshot dependency names and nothing else. It runs what the build published and
/// builds nothing, so it does not take the artifacts of the products this product depends on, nor write the configuration
/// files that a build would read.
/// </remarks>
internal sealed class TestArchivesCiBuildConfiguration : PowershellAdditionalCiBuildConfiguration
{
    public TestArchivesCiBuildConfiguration( string id, string name, string arguments ) : base( id, name, TestArchives.ScriptName, arguments ) { }

    internal override string GetScriptPath( ProductProperties productProperties ) => $"{productProperties.Product.EngineeringDirectory}/{TestArchives.ScriptName}";

    internal override bool PreparesBuildEnvironment => false;
}
