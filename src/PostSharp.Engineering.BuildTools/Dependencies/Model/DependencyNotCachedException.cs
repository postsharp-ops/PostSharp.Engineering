// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools.Dependencies.Model
{
    /// <summary>
    /// Thrown when the artifacts of a dependency are missing from the local cache and the <c>--cached-only</c> option
    /// forbids downloading them.
    /// </summary>
    /// <remarks>
    /// This is deliberately an exception rather than an error-and-return-false. A cache miss under
    /// <c>--cached-only</c> means the caller's assumption about the machine was wrong, which is a different kind of
    /// failure from a build that legitimately did not succeed, and it surfaces with a distinct process exit code.
    /// </remarks>
    [Serializable]
    public class DependencyNotCachedException : Exception
    {
        public DependencyNotCachedException() { }

        public DependencyNotCachedException( string message ) : base( message ) { }

        public DependencyNotCachedException( string message, Exception inner ) : base( message, inner ) { }
    }
}
