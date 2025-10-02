#if !NET7_0_OR_GREATER
using System.ComponentModel;

namespace System.Runtime.CompilerServices
{
    /// <summary>Specifies that a type has required members or that a member is required.</summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = false )]
    [EditorBrowsable( EditorBrowsableState.Never )]
#if EMBED_SYSTEM_TYPES
[Microsoft.CodeAnalysis.Embedded]
#endif
    internal sealed class RequiredMemberAttribute : Attribute { }
}
#endif