// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Docker;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// Checks the invariants of every <see cref="VisualStudioBuildToolsComponentVersion"/> declared by the class, whether it
/// is a build-specific field or a line property. An entry that breaks one of them still compiles, and fails only when a
/// Docker image is built, which runs on an agent and takes hours.
/// </summary>
public sealed class VisualStudioBuildToolsComponentVersionTests
{
    private const BindingFlags _declared = BindingFlags.Public | BindingFlags.Static;

    // The versions are identified by member name rather than by instance, because xUnit serializes theory data and
    // VisualStudioBuildToolsComponentVersion is not serializable. The name is also what a failure reports.
    public static TheoryData<string> VersionNames
    {
        get
        {
            var data = new TheoryData<string>();

            foreach ( var member in GetVersionMembers() )
            {
                data.Add( member.Name );
            }

            return data;
        }
    }

    public static TheoryData<string> SelectorNames
    {
        get
        {
            var data = new TheoryData<string>();

            foreach ( var property in GetSelectorProperties() )
            {
                data.Add( property.Name );
            }

            return data;
        }
    }

    private static FieldInfo[] GetBuildFields()
        => typeof(VisualStudioBuildToolsComponentVersion)
            .GetFields( _declared )
            .Where( f => f.FieldType == typeof(VisualStudioBuildToolsComponentVersion) )
            .ToArray();

    private static PropertyInfo[] GetSelectorProperties()
        => typeof(VisualStudioBuildToolsComponentVersion)
            .GetProperties( _declared )
            .Where( p => p.PropertyType == typeof(VisualStudioBuildToolsComponentVersion) )
            .ToArray();

    private static MemberInfo[] GetVersionMembers() => [..GetBuildFields(), ..GetSelectorProperties()];

    private static VisualStudioBuildToolsComponentVersion GetVersion( string name )
    {
        var member = GetVersionMembers().Single( m => m.Name == name );

        var value = member switch
        {
            FieldInfo field => field.GetValue( null ),
            PropertyInfo property => property.GetValue( null ),
            _ => throw new InvalidOperationException( $"Unexpected member kind for {name}." )
        };

        return (VisualStudioBuildToolsComponentVersion) value!;
    }

    /// <summary>
    /// The resource name is derived from the version and the channel, so an embedded channel manifest saved under a
    /// different name is not found at all.
    /// </summary>
    [Theory]
    [MemberData( nameof(VersionNames) )]
    public void ChannelManifestIsEmbedded( string name )
    {
        var version = GetVersion( name );
        var resourceName = $"PostSharp.Engineering.BuildTools.Resources.{version.ManifestFilename}";

        Assert.Contains( resourceName, typeof(VisualStudioBuildToolsComponent).Assembly.GetManifestResourceNames() );
    }

    /// <summary>
    /// The bootstrapper must belong to the same product line as the channel, because a Dev17 bootstrapper cannot
    /// install a Dev18 channel.
    /// </summary>
    [Theory]
    [MemberData( nameof(VersionNames) )]
    public void BootstrapperMatchesMajorVersion( string name )
    {
        var version = GetVersion( name );

        Assert.StartsWith( $"https://aka.ms/vs/{version.MajorVersion}/", version.BootstrapperUri, StringComparison.Ordinal );
    }

    /// <summary>
    /// A selector property must resolve to a build that Microsoft still services. This is what a re-pin that adds a
    /// build but forgets to move the property would break, and these properties are the members that products are
    /// told to use.
    /// </summary>
    [Theory]
    [MemberData( nameof(SelectorNames) )]
    public void SelectorResolvesToServicedBuild( string name )
    {
        var version = GetVersion( name );

        var field = Assert.Single( GetBuildFields(), f => ReferenceEquals( f.GetValue( null ), version ) );
        var obsolete = field.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotEqual(
            VisualStudioBuildToolsComponentVersion.UnservicedBuildDiagnosticId,
            obsolete?.DiagnosticId );
    }

    /// <summary>
    /// LatestStable and LatestPreview name a channel, so each has to resolve to a build of that channel. The channel
    /// decides the channel id given to the installer, and a Preview build reached through LatestStable would only be
    /// noticed once an image was built.
    /// </summary>
    [Theory]
    [InlineData( nameof(VisualStudioBuildToolsComponentVersion.LatestStable), "Release" )]
    [InlineData( nameof(VisualStudioBuildToolsComponentVersion.LatestPreview), "Preview" )]
    public void LatestSelectorMatchesChannel( string name, string channel )
    {
        Assert.Equal( channel, GetVersion( name ).Channel );
    }

    /// <summary>
    /// Every build-specific field is obsolete, so that a product is directed to a line property. The two diagnostics
    /// are distinct, because an unserviced build is a defect to correct and a pinned build is merely discouraged.
    /// </summary>
    [Fact]
    public void EveryBuildFieldIsObsolete()
    {
        string[] expected =
        [
            VisualStudioBuildToolsComponentVersion.UnservicedBuildDiagnosticId,
            VisualStudioBuildToolsComponentVersion.SpecificBuildDiagnosticId
        ];

        foreach ( var field in GetBuildFields() )
        {
            var obsolete = field.GetCustomAttribute<ObsoleteAttribute>();

            Assert.NotNull( obsolete );
            Assert.Contains( obsolete.DiagnosticId, expected );
        }
    }
}
