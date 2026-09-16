#if !NET8_0_OR_GREATER
using Microsoft.CodeAnalysis;

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

/// <summary>
/// Indicates that the instance's storage is sequentially replicated "length" times.
/// </summary>
/// <remarks>
/// <para>
/// This attribute can be used to annotate a <see langword="struct"/> type with a single field.
/// The runtime will replicate that field in the actual type layout as many times as is specified.
/// </para>
/// <para>
/// Here's an example of how an inline array type with 8 <see cref="float"/> values can be declared:
/// <code lang="csharp">
/// [InlineArray(8)]
/// struct Float8InlineArray
/// {
///     private float _value;
/// }
/// </code>
/// </para>
/// </remarks>
#if EMBED_SYSTEM_TYPES
[Embedded]
#endif
[AttributeUsage( AttributeTargets.Struct, AllowMultiple = false )]
internal sealed class InlineArrayAttribute : Attribute
{
    /// <summary>Creates a new <see cref="InlineArrayAttribute"/> instance with the specified length.</summary>
    /// <param name="length">The number of sequential fields to replicate in the inline array type.</param>
    public InlineArrayAttribute( int length )
    {
        Length = length;
    }

    /// <summary>Gets the number of sequential fields to replicate in the inline array type.</summary>
    public int Length { get; }
}
#endif
