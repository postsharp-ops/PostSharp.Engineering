using Microsoft.CodeAnalysis;

#if !NET6_0_OR_GREATER

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

[AttributeUsage( AttributeTargets.Parameter )]
#if EMBED_SYSTEM_TYPES
[Embedded]
#endif
internal sealed class InterpolatedStringHandlerArgumentAttribute : Attribute
{
    public InterpolatedStringHandlerArgumentAttribute( string argument )
    {
        Arguments = new[] { argument };
    }

    public InterpolatedStringHandlerArgumentAttribute( params string[] arguments )
    {
        Arguments = arguments;
    }

    public string[] Arguments { get; }
}
#endif