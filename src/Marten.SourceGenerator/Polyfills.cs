// Polyfills required to use modern C# language features (records, init-only setters)
// from a netstandard2.0 source-generator project. These types are normally provided by
// the BCL on net5+/netstandard2.1+; on netstandard2.0 the C# compiler still synthesizes
// references to them but the assembly metadata isn't there. Defining the markers
// ourselves is the standard workaround for analyzer-only projects.

#if NETSTANDARD2_0

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }

#endif
