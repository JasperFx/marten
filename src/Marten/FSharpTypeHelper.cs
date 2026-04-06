using System;

namespace Marten;

/// <summary>
/// Reflection-based helper for detecting and constructing F# types without
/// a compile-time dependency on FSharp.Core. If FSharp.Core is loaded in
/// the AppDomain (e.g., by an F# consuming project), F# support is enabled
/// automatically. Otherwise, all F# checks gracefully return false/null.
/// </summary>
internal static class FSharpTypeHelper
{
    private static readonly Lazy<Type?> FSharpOptionOpenGeneric = new(() =>
        Type.GetType("Microsoft.FSharp.Core.FSharpOption`1, FSharp.Core"));

    /// <summary>
    /// True if FSharp.Core is loaded and FSharpOption is available.
    /// </summary>
    public static bool IsFSharpCoreAvailable => FSharpOptionOpenGeneric.Value != null;

    /// <summary>
    /// Returns the open generic FSharpOption&lt;&gt; type, or null if FSharp.Core is not loaded.
    /// </summary>
    public static Type? GetFSharpOptionOpenType() => FSharpOptionOpenGeneric.Value;

    /// <summary>
    /// Check if a type is FSharpOption&lt;T&gt; for any T.
    /// </summary>
    public static bool IsFSharpOptionType(Type type) =>
        FSharpOptionOpenGeneric.Value != null
        && type.IsGenericType
        && type.GetGenericTypeDefinition() == FSharpOptionOpenGeneric.Value;

    /// <summary>
    /// Construct a closed FSharpOption&lt;T&gt; type for the given inner type.
    /// Returns null if FSharp.Core is not loaded.
    /// </summary>
    public static Type? MakeFSharpOptionType(Type innerType) =>
        FSharpOptionOpenGeneric.Value?.MakeGenericType(innerType);

    /// <summary>
    /// Check if a type matches FSharpOption&lt;T&gt; for a specific inner type T.
    /// </summary>
    public static bool IsFSharpOptionOf(Type type, Type innerType)
    {
        var expected = MakeFSharpOptionType(innerType);
        return expected != null && type == expected;
    }
}
