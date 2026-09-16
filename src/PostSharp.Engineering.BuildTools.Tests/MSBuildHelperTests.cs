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
}
