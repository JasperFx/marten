#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Internal.CompiledQueries;

/// <summary>
/// Process-wide registry of source-generator-emitted compiled query handlers.
/// Populated implicitly at assembly load by <c>[ModuleInitializer]</c> blocks
/// emitted by <c>Marten.SourceGenerator</c> for every
/// <c>ICompiledQuery&lt;TDoc, TOut&gt;</c> implementation discovered in an
/// assembly marked with <c>[JasperFxAssembly]</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the implicit-opt-in surface for #4405's compiled-query source-gen
/// PoC. The consumer never calls <see cref="Register"/> directly — the
/// generator does it from per-query <c>[ModuleInitializer]</c> shims. The
/// runtime side (<c>CompiledQueryCollection.GetCompiledQuerySourceFor</c>)
/// consults the registry on first use of each query type. If a descriptor is
/// present, the source-gen path is taken; if not, the PoC bridge currently
/// falls through to <c>JasperFx.RuntimeCompiler</c>. The bridge is removed
/// when iteration 4 lands green — at that point a miss throws.
/// </para>
/// <para>
/// The registry is intentionally a process-wide static. Compiled query
/// handlers are pure functions of the user's query type and do not capture
/// any per-store state, so a single registration suffices regardless of how
/// many <c>DocumentStore</c> instances exist. Registration is idempotent:
/// re-registering the same query type replaces the previous descriptor,
/// which keeps generator-rerun + hot-reload scenarios well-defined.
/// </para>
/// </remarks>
public static class CompiledQueryHandlerRegistry
{
    private static readonly ConcurrentDictionary<Type, CompiledQueryHandlerDescriptor> s_handlers = new();

    /// <summary>
    /// Registers a handler descriptor for <paramref name="queryType"/>. Called by
    /// generator-emitted <c>[ModuleInitializer]</c> shims at assembly load. Idempotent.
    /// </summary>
    public static void Register(Type queryType, CompiledQueryHandlerDescriptor descriptor)
    {
        if (queryType == null) throw new ArgumentNullException(nameof(queryType));
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        s_handlers[queryType] = descriptor;
    }

    /// <summary>
    /// Attempts to retrieve a handler descriptor for <paramref name="queryType"/>.
    /// Returns <see langword="false"/> if the consumer hasn't referenced
    /// <c>Marten.SourceGenerator</c> or hasn't decorated the defining assembly
    /// with <c>[JasperFxAssembly]</c>.
    /// </summary>
    public static bool TryGet(Type queryType, [NotNullWhen(true)] out CompiledQueryHandlerDescriptor? descriptor)
    {
        if (queryType == null) throw new ArgumentNullException(nameof(queryType));
        return s_handlers.TryGetValue(queryType, out descriptor);
    }

    /// <summary>
    /// Returns the number of registered handlers. Diagnostic/test surface only;
    /// not part of the hot path. Useful for asserting in iteration 3/4 tests that
    /// the module initializers fired against a generated assembly.
    /// </summary>
    public static int Count => s_handlers.Count;

    /// <summary>
    /// Removes a registered descriptor and returns the previous descriptor (or null
    /// if none was registered). Diagnostic/test surface only — useful for perf
    /// A/B tests that need to flip a single query type between the source-gen and
    /// codegen-bridge paths within a single process.
    /// </summary>
    /// <remarks>
    /// Not part of the normal lifecycle. Production code paths never call this —
    /// once a descriptor is registered by an assembly's <c>[ModuleInitializer]</c>,
    /// it stays for the life of the process.
    /// </remarks>
    public static CompiledQueryHandlerDescriptor? Unregister(Type queryType)
    {
        if (queryType == null) throw new ArgumentNullException(nameof(queryType));
        return s_handlers.TryRemove(queryType, out var removed) ? removed : null;
    }
}
