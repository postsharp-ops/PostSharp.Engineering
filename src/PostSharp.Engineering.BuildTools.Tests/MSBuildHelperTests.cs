// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.MSBuild;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

// ReSharper disable once InconsistentNaming
public sealed class MSBuildHelperTests
{
    /// <summary>
    /// Calls <see cref="MSBuildHelper.InitializeLocator"/> from several threads at once. MSBuildLocator accepts one
    /// registration per process, so before the initialization was locked, two threads could both pass its
    /// <c>CanRegister</c> check and the second registration failed.
    /// </summary>
    /// <remarks>
    /// This is a regression guard, not a proof. Every test runs in one process, and once anything has registered a
    /// locator, <c>CanRegister</c> is false and each call here returns without doing any work. The test therefore
    /// exercises the race only when it runs before the other tests that initialize the locator. It still fails if a
    /// later change makes the method throw when several threads enter it.
    /// </remarks>
    [Fact]
    public void InitializeLocatorIsSafeToCallConcurrently()
    {
        const int threadCount = 8;

        // A barrier rather than a delay: every thread is released at the same moment, which is what makes the calls
        // overlap.
        using var barrier = new Barrier( threadCount );
        var exceptions = new ConcurrentBag<Exception>();

        var threads = Enumerable.Range( 0, threadCount )
            .Select(
                _ => new Thread(
                    () =>
                    {
                        barrier.SignalAndWait();

                        try
                        {
                            MSBuildHelper.InitializeLocator();
                        }
                        catch ( Exception e )
                        {
                            exceptions.Add( e );
                        }
                    } ) )
            .ToArray();

        foreach ( var thread in threads )
        {
            thread.Start();
        }

        foreach ( var thread in threads )
        {
            thread.Join();
        }

        Assert.Empty( exceptions );
    }

    /// <summary>
    /// The method is called once per command and again by several tests, so a second call has to be a no-op rather
    /// than a second registration.
    /// </summary>
    [Fact]
    public void InitializeLocatorIsIdempotent()
    {
        MSBuildHelper.InitializeLocator();
        var first = MSBuildHelper.RegisteredInstance;

        MSBuildHelper.InitializeLocator();

        Assert.Same( first, MSBuildHelper.RegisteredInstance );
    }

    /// <summary>
    /// The installations of the machine on which the error of issue #142 was observed, ordered by descending version
    /// the way <see cref="MSBuildHelper.FindMSBuildExe"/> orders them. Visual Studio had updated itself from the
    /// pinned 18.9 to 18.10 and then to 18.11, and had removed 18.9.
    /// </summary>
    private static readonly MSBuildInstance[] _installations =
    [
        new( "SQL Server Management Studio 22", new Version( 22, 10, 12201, 205 ), "22.10.12201.205", @"C:\Ssms", "VisualStudio" ),
        new( "Visual Studio Professional 2026", new Version( 18, 11, 12202, 211 ), "18.11.12202.211", @"C:\Vs2026.11", "VisualStudio" ),
        new( "Visual Studio Professional 2026", new Version( 18, 10, 12210, 168 ), "18.10.12210.168", @"C:\Vs2026.10", "VisualStudio" ),
        new( "Visual Studio Professional 2022", new Version( 17, 14, 37710, 0 ), "17.14.37710.0", @"C:\Vs2022", "VisualStudio" )
    ];

    /// <summary>
    /// An installation of the pinned version is used whenever there is one, and reported as an exact match, whether
    /// or not a newer one is allowed.
    /// </summary>
    [Theory]
    [InlineData( true )]
    [InlineData( false )]
    public void ThePinnedVersionIsPreferredOverANewerOne( bool allowNewer )
    {
        var instance = MSBuildHelper.SelectInstance( _installations, new Version( 18, 10 ), allowNewer, out var isNewer );

        Assert.Equal( @"C:\Vs2026.10", instance?.Path );
        Assert.False( isNewer );
    }

    /// <summary>
    /// The regression. Visual Studio updated past the pinned 18.9 and removed it, which stopped every local build
    /// until the product definition of each repository was edited.
    /// </summary>
    [Fact]
    public void ANewerVersionIsAcceptedWhenThePinnedOneIsGone()
    {
        var instance = MSBuildHelper.SelectInstance( _installations, new Version( 18, 9 ), allowNewer: true, out var isNewer );

        // The newest installation that is at least the pinned version, not merely the first one above it.
        Assert.Equal( @"C:\Vs2026.11", instance?.Path );
        Assert.True( isNewer );
    }

    /// <summary>
    /// Continuous integration keeps the exact match. The container has exactly one installation, and the pin is what
    /// makes a local build comparable to it, so a silent substitution there would defeat the pin.
    /// </summary>
    [Fact]
    public void ANewerVersionIsRefusedWhenNewerIsNotAllowed()
    {
        var instance = MSBuildHelper.SelectInstance( _installations, new Version( 18, 9 ), allowNewer: false, out var isNewer );

        Assert.Null( instance );
        Assert.False( isNewer );
    }

    /// <summary>
    /// An older installation is never a substitute, because the build would run on an engine that does not have what
    /// the pin was raised for.
    /// </summary>
    [Fact]
    public void AnOlderVersionIsNeverSubstituted()
    {
        Assert.Null( MSBuildHelper.SelectInstance( _installations, new Version( 18, 12 ), allowNewer: true, out var isNewer ) );
        Assert.False( isNewer );
    }

    /// <summary>
    /// The substitution stops at the major version. SQL Server Management Studio 22 carries an MSBuild whose version
    /// is numerically above every Visual Studio one, so a fallback that only compared versions would build the
    /// product with it as soon as Visual Studio moved from 18.9 to 18.10.
    /// </summary>
    [Fact]
    public void AnInstallationOfAnotherVersionLineIsNeverSubstituted()
    {
        // No 19.x is installed. 22.10 is numerically above 19.0, and must not be chosen for it.
        Assert.Null( MSBuildHelper.SelectInstance( _installations, new Version( 19, 0 ), allowNewer: true, out var isNewer ) );
        Assert.False( isNewer );
    }

    /// <summary>
    /// The substitution is not specific to the most recent version line: a repository pinned to an older line gets
    /// the newest installation of that line.
    /// </summary>
    [Fact]
    public void TheSubstituteComesFromThePinnedVersionLine()
    {
        var instance = MSBuildHelper.SelectInstance( _installations, new Version( 17, 0 ), allowNewer: true, out var isNewer );

        Assert.Equal( @"C:\Vs2022", instance?.Path );
        Assert.True( isNewer );
    }

    /// <summary>
    /// A component that the pin leaves out matches anything, which is what makes a pin of '18.9' accept the build
    /// number that Visual Studio actually installs.
    /// </summary>
    [Fact]
    public void AnOmittedVersionComponentMatchesAnything()
    {
        var instance = MSBuildHelper.SelectInstance( _installations, new Version( 18, 11 ), allowNewer: false, out var isNewer );

        Assert.Equal( @"C:\Vs2026.11", instance?.Path );
        Assert.False( isNewer );
    }
}
