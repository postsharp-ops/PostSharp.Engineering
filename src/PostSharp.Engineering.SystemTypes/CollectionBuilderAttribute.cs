#if !NET8_0_OR_GREATER
using Microsoft.CodeAnalysis;

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

/// <summary>Indicates the builder type and method that the compiler uses to construct a collection from a collection expression.</summary>
#if EMBED_SYSTEM_TYPES
[Embedded]
#endif
[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false )]
internal sealed class CollectionBuilderAttribute : Attribute
{
    // ReadOnlySpan<T> is written as <c> rather than as a <see cref>, which the original file in dotnet/runtime uses.
    // SystemTypes.props compiles this file into the consuming project, whose target framework may be one that does not
    // declare ReadOnlySpan<T>, such as netstandard2.0. The reference then cannot resolve and the compiler reports
    // CS1574, which a repository that produces XML documentation and treats warnings as errors reports as an error.
    /// <summary>Initialize the attribute to refer to the <paramref name="methodName"/> method on the <paramref name="builderType"/> type.</summary>
    /// <param name="builderType">The type of the builder to use to construct the collection.</param>
    /// <param name="methodName">The name of the method on the builder to use to construct the collection.</param>
    /// <remarks>
    /// <paramref name="methodName"/> must refer to a static method that accepts a single parameter of
    /// type <c>ReadOnlySpan&lt;T&gt;</c> and returns an instance of the collection being built containing
    /// a copy of the data from that span.  In future releases of .NET, additional patterns may be supported.
    /// </remarks>
    public CollectionBuilderAttribute( Type builderType, string methodName )
    {
        BuilderType = builderType;
        MethodName = methodName;
    }

    /// <summary>Gets the type of the builder to use to construct the collection.</summary>
    public Type BuilderType { get; }

    /// <summary>Gets the name of the method on the builder to use to construct the collection.</summary>
    /// <remarks>This should match the metadata name of the target method. For example, this might be ".ctor" if targeting the type's constructor.</remarks>
    public string MethodName { get; }
}
#endif
