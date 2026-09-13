#if !NET11_0_OR_GREATER
using System.ComponentModel;
using Microsoft.CodeAnalysis;

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

/// <summary>
/// Reserved for use by a compiler for tracking metadata.
/// This attribute should not be used by developers in source code.
/// </summary>
#if EMBED_SYSTEM_TYPES
[Embedded]
#endif
[EditorBrowsable( EditorBrowsableState.Never )]
[AttributeUsage( AttributeTargets.Class, Inherited = false )]
internal sealed class IsClosedTypeAttribute : Attribute
{
    private Type[] _derivedTypes = Type.EmptyTypes;

    /// <summary>Initializes the attribute.</summary>
    public IsClosedTypeAttribute() { }

    /// <summary>Gets or sets the derived types of the closed type.</summary>
    /// <value>An array of the derived types of the closed type. A <see langword="null"/> value is normalized to an empty array.</value>
    public Type[] DerivedTypes
    {
        get => _derivedTypes;
        set => _derivedTypes = value ?? Type.EmptyTypes;
    }
}
#endif
