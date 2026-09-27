// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using System.Collections.Immutable;

namespace PostSharp.Engineering.BuildTools.Build.Testing;

/// <summary>
/// The conventions of the test archives of a product whose <see cref="Product.PublishTestArchives"/> is set. See
/// <c>doc/testing-platform.md</c>.
/// </summary>
internal static class TestArchives
{
    /// <summary>
    /// The name of the script that <c>generate-scripts</c> writes into the engineering directory, and that runs the archives.
    /// </summary>
    public const string ScriptName = "RunTests.ps1";

    /// <summary>
    /// The directory of the archives, relative to the repository root. <c>TestArchive.targets</c> writes them there unless a
    /// project sets <c>TestArchiveDirectory</c>.
    /// </summary>
    public const string Directory = "artifacts/tests";

    /// <summary>
    /// Adds <c>PublishTestArchive=true</c> to the properties of the build of the solutions of a product that publishes test
    /// archives, unless the command line sets it. A build in the IDE does not set it, so it does not spend the time of a
    /// publication on every build.
    /// </summary>
    public static BuildSettings AddBuildProperties( Product product, BuildSettings settings )
        => product.PublishTestArchives && !settings.Properties.ContainsKey( "PublishTestArchive" )
            ? settings.WithAdditionalProperties( ImmutableDictionary<string, string>.Empty.Add( "PublishTestArchive", "true" ) )
            : settings;
}
