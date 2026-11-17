#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Marten.Exceptions;

namespace Marten.Services;

/// <summary>
///     #5624. Turns System.Text.Json's "annotate the type" advice into advice the consumer can act
///     on, for the one type they cannot annotate.
/// </summary>
/// <remarks>
///     <para>
///         A <c>Select(x =&gt; new { ... })</c> projection is materialized by handing the projected
///         type — a compiler-generated anonymous type — to the serializer.
///         <c>[JsonSerializable(typeof(...))]</c> needs a nameable type, so no
///         <c>JsonSerializerContext</c> can ever carry metadata for
///         <c>&lt;&gt;f__AnonymousType0&lt;string, int&gt;</c>. If a resolver that replaced the
///         reflection-based one is in play, the query fails with
///         <c>NotSupportedException: JsonTypeInfo metadata for type '&lt;&gt;f__AnonymousType0`2'
///         was not provided</c>, whose own remedy is impossible to follow.
///     </para>
///     <para>
///         The flag is a static on a generic type, so the anonymity test runs once per projected
///         type rather than once per row, and the whole thing costs nothing until System.Text.Json
///         has already decided to throw.
///     </para>
/// </remarks>
internal static class AnonymousProjection<T>
{
    /// <summary>
    ///     Is <typeparamref name="T" /> a compiler-generated anonymous type?
    /// </summary>
    internal static readonly bool Yes = IsAnonymous(typeof(T));

    private static bool IsAnonymous(Type type)
    {
        return type.IsGenericType
               && type.IsDefined(typeof(CompilerGeneratedAttribute), false)
               && type.Name.Contains("AnonymousType", StringComparison.Ordinal);
    }

    internal static MartenNotSupportedException Explain(JsonSerializerOptions options, Exception inner)
    {
        var resolver = options.TypeInfoResolver?.GetType().FullName ?? "the configured TypeInfoResolver";

        return new MartenNotSupportedException(
            $"Cannot materialize the LINQ projection into the anonymous type '{typeof(T)}' because {resolver} has no JsonTypeInfo for it, and an anonymous type cannot be named by a [JsonSerializable] attribute. Either project into a named type that IS registered with your JsonSerializerContext, or layer the context with SystemTextJsonSerializer.UseTypeInfoResolver(context) rather than assigning JsonSerializerOptions.TypeInfoResolver, which keeps a reflection-based resolver behind it. See https://martendb.io/configuration/aot-publishing.",
            inner);
    }
}
