#if !NET11_0_OR_GREATER
using System.ComponentModel;
using Microsoft.CodeAnalysis;

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

/// <summary>Indicates the version of the memory safety rules used when the module was compiled.</summary>
#if EMBED_SYSTEM_TYPES
[Embedded]
#endif
[EditorBrowsable( EditorBrowsableState.Never )]
[AttributeUsage( AttributeTargets.Module, Inherited = false, AllowMultiple = false )]
internal sealed class MemorySafetyRulesAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="MemorySafetyRulesAttribute"/> class.</summary>
    /// <param name="version">The version of the memory safety rules used when the module was compiled.</param>
    public MemorySafetyRulesAttribute( int version ) => Version = version;

    /// <summary>Gets the version of the memory safety rules used when the module was compiled.</summary>
    public int Version { get; }
}
#endif
