using Microsoft.CodeAnalysis;

#if !NET6_0_OR_GREATER // ReSharper disable once CheckNamespace

namespace System.Runtime.CompilerServices;

[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct, Inherited = false )]
#if EMBED_SYSTEM_TYPES
[Embedded]
#endif
internal sealed class InterpolatedStringHandlerAttribute : Attribute { }
#endif