// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools;

/// <summary>
/// Marks an environment variable field as containing a secret value that should be masked when passed to Claude Code.
/// </summary>
[AttributeUsage( AttributeTargets.Field )]
internal class SecretAttribute : Attribute { }
