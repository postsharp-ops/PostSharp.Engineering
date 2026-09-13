#if !NET11_0_OR_GREATER
using Microsoft.CodeAnalysis;

// ReSharper disable once CheckNamespace
namespace System.Diagnostics.CodeAnalysis;

/// <summary>
/// Indicates that the specified member requires the caller to be in an unsafe context.
/// </summary>
#if EMBED_SYSTEM_TYPES
[Embedded]
#endif
[AttributeUsage(
    AttributeTargets.Constructor | AttributeTargets.Event | AttributeTargets.Method | AttributeTargets.Property,
    Inherited = false,
    AllowMultiple = false )]
internal sealed class RequiresUnsafeAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiresUnsafeAttribute"/> class.
    /// </summary>
    public RequiresUnsafeAttribute() { }
}
#endif
