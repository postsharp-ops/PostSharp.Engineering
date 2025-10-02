#if !NETCOREAPP

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

#if EMBED_SYSTEM_TYPES
[Microsoft.CodeAnalysis.Embedded]
#endif
[AttributeUsage( AttributeTargets.Parameter )]
internal sealed class CallerArgumentExpressionAttribute : Attribute
{
    public CallerArgumentExpressionAttribute( string parameterName )
    {
        ParameterName = parameterName;
    }

    public string ParameterName { get; }
}

#endif