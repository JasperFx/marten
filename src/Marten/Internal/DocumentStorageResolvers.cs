#nullable enable
using System;
using System.Collections.Concurrent;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;

namespace Marten.Internal;

/// <summary>
///     AOT-safe bridge from a runtime <see cref="Type" /> to the closed generic
///     <c>StorageFor&lt;T&gt;()</c> calls.
/// </summary>
/// <remarks>
///     <para>
///         Several places in the LINQ pipeline only know the document type as a
///         <see cref="Type" /> — <c>LinqQueryParser.BuildStatements()</c> works off
///         <c>CollectionUsage.ElementType</c>, includes work off the generic arguments of the
///         user-supplied receiver, and the document cleaner works off the registered mappings.
///         Historically those went through <c>CloseAndBuildAs</c>, i.e.
///         <c>MakeGenericType</c> + <c>Activator.CreateInstance</c> over a private
///         <c>StorageFinder&lt;T&gt;</c> shim.
///     </para>
///     <para>
///         Under Native AOT that shim's instantiation over a user document type is never
///         statically reachable, so the runtime has no native code for it and
///         <c>Activator.CreateInstance</c> throws
///         <c>MissingMethodException: No parameterless constructor defined for type
///         'QuerySession+StorageFinder`1[TDoc]'</c> on the first read (#5328).
///     </para>
///     <para>
///         Every document type reaches Marten through *some* generic entry point first —
///         <c>Query&lt;T&gt;()</c>, <c>Include&lt;TInclude&gt;()</c>, <c>Store&lt;T&gt;()</c>,
///         <c>LoadAsync&lt;T&gt;()</c> — and at that point the compiler has already emitted the
///         closed instantiation. Those entry points call <see cref="Register{T}" />, which
///         captures the two closed-generic lookups in static lambdas. The <see cref="Type" />-keyed
///         sites then resolve through this registry with no runtime code generation at all.
///     </para>
///     <para>
///         The registry is a fast path, not a contract: a miss falls back to the reflective
///         <c>CloseAndBuildAs</c> shim, which is still correct everywhere a JIT is available.
///     </para>
/// </remarks>
internal static class DocumentStorageResolvers
{
    private static readonly ConcurrentDictionary<Type, Resolver> _resolvers = new();

    /// <summary>
    ///     Record the closed-generic storage lookups for <typeparamref name="T" />. Called from the
    ///     generic entry points, where the instantiation is statically reachable.
    /// </summary>
    internal static void Register<T>() where T : notnull
    {
        // ContainsKey first so the steady state is a lookup rather than a delegate allocation.
        if (_resolvers.ContainsKey(typeof(T)))
        {
            return;
        }

        _resolvers[typeof(T)] = new Resolver(
            static session => session.StorageFor<T>(),
            static providers => providers.StorageFor<T>().Lightweight);
    }

    internal static IDocumentStorage? TryResolve(Type documentType, QuerySession session)
    {
        return _resolvers.TryGetValue(documentType, out var resolver) ? resolver.FromSession(session) : null;
    }

    internal static IDocumentStorage? TryResolve(Type documentType, IProviderGraph providers)
    {
        return _resolvers.TryGetValue(documentType, out var resolver) ? resolver.FromProviders(providers) : null;
    }

    private sealed record Resolver(
        Func<QuerySession, IDocumentStorage> FromSession,
        Func<IProviderGraph, IDocumentStorage> FromProviders);
}
