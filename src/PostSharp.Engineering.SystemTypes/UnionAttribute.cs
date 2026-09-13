#if !NET11_0_OR_GREATER
using Microsoft.CodeAnalysis;

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

/// <summary>
/// Indicates that a class or struct is a union type, enabling compiler support for union behaviors.
/// </summary>
/// <remarks>
/// <para>
/// Any class or struct annotated with this attribute is recognized by the C# compiler as a union type.
/// Union types may support behaviors such as implicit conversions from case types, pattern matching
/// that unwraps the union's contents, and switch exhaustiveness checking.
/// </para>
/// </remarks>
/// <seealso cref="IUnion"/>
#if EMBED_SYSTEM_TYPES
[Embedded]
#endif
[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false )]
internal sealed class UnionAttribute : Attribute { }
#endif
