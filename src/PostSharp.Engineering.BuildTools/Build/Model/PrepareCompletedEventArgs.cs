// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;

namespace PostSharp.Engineering.BuildTools.Build.Model;

/// <summary>
/// Arguments of the <see cref="Product.PrepareCompleted"/> event.
/// </summary>
[PublicAPI]
public class PrepareCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the current <see cref="BuildContext"/>.
    /// </summary>
    public BuildContext Context { get; }

    /// <summary>
    /// Gets the current <see cref="BuildSettings"/>.
    /// </summary>
    public BuildSettings Settings { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the processing of the event failed. If an event handler
    /// sets this property to <c>true</c>, the build fails.
    /// </summary>
    public bool IsFailed { get; set; }

    internal PrepareCompletedEventArgs( BuildContext context, BuildSettings settings )
    {
        this.Context = context;
        this.Settings = settings;
    }
}