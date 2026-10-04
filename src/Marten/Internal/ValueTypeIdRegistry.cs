#nullable enable
using System;
using System.Collections.Concurrent;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Internal.ClosedShape;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Schema.Identity;
using Weasel.Core.Identity;

namespace Marten.Internal;

/// <summary>
///     Statically-closed factories for the generic types Marten would otherwise close over a strong-typed id
///     at runtime. Registry first, reflective fallback — the same shape as
///     <see cref="DocumentStorageResolvers" /> (#5328).
/// </summary>
/// <remarks>
///     <para>
///         #5589. The identity path closes a chain of generics over the caller's id type through
///         <c>CloseAndBuildAs</c> / <c>MakeGenericMethod</c>, which is <see cref="Activator" /> on a type
///         computed at runtime. A Native AOT image has no instantiation for any of them unless something
///         rooted it statically, so a single strong-typed id anywhere in the store used to kill the
///         application while the mapping for the <i>first</i> document was being built.
///     </para>
///     <para>
///         Registering through the generic <see cref="Register{TDoc,TWrapper,TInner}" /> is what roots them:
///         the compiler emits every instantiation named in its body, and the delegates stored here hand the
///         runtime those exact closed types instead of asking the reflection stack to produce them. Nothing
///         here changes behaviour under a JIT — a miss simply falls through to the old reflective path, which
///         is still correct everywhere dynamic code is available.
///     </para>
///     <para>
///         The registry is static rather than per-store because the instantiations are a fact about the
///         compiled image, not about a <see cref="StoreOptions" />. That also lets a future
///         <c>Marten.SourceGenerator</c> emit the registrations from a <c>[ModuleInitializer]</c> without
///         needing a store to hang them off.
///     </para>
///     <para>
///         Class wrappers — F# single-case discriminated unions — are not covered: their select clause is
///         <c>FSharpDiscriminatedUnionIdSelectClause</c> rather than
///         <see cref="ValueTypeIdSelectClause{TOuter,TInner}" />, whose <c>where TOuter : struct</c> is what
///         the constraint below mirrors.
///     </para>
/// </remarks>
internal static class ValueTypeIdRegistry
{
    private static readonly ConcurrentDictionary<(Type Wrapper, Type Inner),
        Func<ValueTypeIdGeneration, IScalarSelectClause>> s_idSelectClauses = new();

    private static readonly ConcurrentDictionary<(Type Wrapper, Type Inner),
        Func<MemberInfo, IStrongTypedIdGeneration, IQueryableMember>> s_idMembers = new();

    private static readonly ConcurrentDictionary<(Type Document, Type Wrapper), object> s_providers = new();

    private static readonly ConcurrentDictionary<Type, Func<QuerySession.ILoader>> s_loaders = new();

    /// <summary>
    ///     Roots every instantiation the identity path needs for one document type and its strong-typed id.
    /// </summary>
    public static void Register<TDoc, TWrapper, TInner>()
        where TDoc : notnull
        where TWrapper : struct
        where TInner : notnull
    {
        var byId = (typeof(TWrapper), typeof(TInner));

        s_idSelectClauses[byId] = generation => new ValueTypeIdSelectClause<TWrapper, TInner>(generation);

        s_idMembers[byId] = (member, generation) => new StrongTypedIdMember<TWrapper, TInner>(member, generation);

        s_loaders[typeof(TWrapper)] = static () => QuerySession.BuildLoader<TWrapper>();

        s_providers[(typeof(TDoc), typeof(TWrapper))] = new Func<DocumentMapping, ValueTypeInfo,
            DocumentProvider<TDoc>>((mapping, vt) =>
            ClosedShapeRegistration.BuildProviderFor<TDoc, TWrapper>(mapping,
                new ValueTypeIdentification<TDoc, TWrapper, TInner>(mapping.IdMember!, vt, mapping.DocumentType)));
    }

    public static QuerySession.ILoader? TryLoader(Type wrapper)
    {
        return s_loaders.TryGetValue(wrapper, out var factory) ? factory() : null;
    }

    public static IScalarSelectClause? TryIdSelectClause(Type wrapper, Type inner,
        ValueTypeIdGeneration generation)
    {
        return s_idSelectClauses.TryGetValue((wrapper, inner), out var factory) ? factory(generation) : null;
    }

    public static IQueryableMember? TryIdMember(Type wrapper, Type inner, MemberInfo member,
        IStrongTypedIdGeneration generation)
    {
        return s_idMembers.TryGetValue((wrapper, inner), out var factory) ? factory(member, generation) : null;
    }

    public static DocumentProvider<TDoc>? TryProvider<TDoc>(Type wrapper, DocumentMapping mapping, ValueTypeInfo vt)
        where TDoc : notnull
    {
        return s_providers.TryGetValue((typeof(TDoc), wrapper), out var factory)
            ? ((Func<DocumentMapping, ValueTypeInfo, DocumentProvider<TDoc>>)factory)(mapping, vt)
            : null;
    }
}
