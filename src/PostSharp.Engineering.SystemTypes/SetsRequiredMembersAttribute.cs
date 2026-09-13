#if !NET7_0_OR_GREATER
using Microsoft.CodeAnalysis;

// ReSharper disable once CheckNamespace
namespace System.Diagnostics.CodeAnalysis;

/// <summary>
/// Specifies that this constructor sets all required members for the current type, and callers
/// do not need to set any required members themselves.
/// </summary>
#if EMBED_SYSTEM_TYPES
[Embedded]
#endif
[AttributeUsage( AttributeTargets.Constructor, AllowMultiple = false, Inherited = false )]
internal sealed class SetsRequiredMembersAttribute : Attribute { }
#endif
