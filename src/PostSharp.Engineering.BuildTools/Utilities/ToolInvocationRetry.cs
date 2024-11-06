// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Utilities;

[PublicAPI]
public record ToolInvocationRetry( Regex? Regex, int? ExitCode );