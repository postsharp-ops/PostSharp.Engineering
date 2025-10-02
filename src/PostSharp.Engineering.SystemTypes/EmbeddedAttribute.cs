#if EMBED_SYSTEM_TYPES
using System;

namespace Microsoft.CodeAnalysis
{
    [AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface | AttributeTargets.Delegate )]
    internal sealed partial class EmbeddedAttribute : Attribute
    {
    }
}
#endif