#if !NET5_0_OR_GREATER
using System.ComponentModel;
using System.Reflection;

// ReSharper disable All

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Reserved to be used by the compiler for tracking metadata.
    /// This class should not be used by developers in source code.
    /// </summary>
#if EMBED_SYSTEM_TYPES
[Microsoft.CodeAnalysis.Embedded]
#endif
    [EditorBrowsable( EditorBrowsableState.Never )]
    [Obfuscation( Exclude = true )]
    internal static class IsExternalInit { }
}
#else
using System.Runtime.CompilerServices;

[assembly: TypeForwardedTo( typeof(IsExternalInit) )]
#endif