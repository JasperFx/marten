#nullable enable
using System;
using System.Collections.Concurrent;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

/// <summary>
///     AOT-safe bridge from a runtime <see cref="Type" /> to the select clauses the LINQ pipeline
///     closes over a <em>projected</em> type rather than over a document type.
/// </summary>
/// <remarks>
///     <para>
///         The sibling of <see cref="Marten.Internal.DocumentStorageResolvers" />, for the same
///         reason (#5328) one step later in the pipeline. A <c>Select()</c> projection is parsed
///         from an expression tree, so the only thing <see cref="Parsing.SelectorVisitor" /> and
///         <see cref="CollectionUsage" /> have is <c>selectExpression.Type</c> — a
///         <see cref="Type" />. Both used <c>CloseAndBuildAs</c>, i.e. <c>MakeGenericType</c> +
///         <c>Activator.CreateInstance</c>, and under Native AOT no instantiation of
///         <c>SelectDataSelectClause&lt;TProjection&gt;</c> is statically reachable, so
///         <b>every</b> object projection — anonymous or named — died on
///         <c>MissingMethodException: No parameterless constructor defined for type
///         'SelectDataSelectClause`1[YourDto]'</c> before the query was ever sent (#5624).
///     </para>
///     <para>
///         Every projected type does reach Marten through a generic entry point first:
///         <c>Queryable.Select&lt;TSource, TResult&gt;</c> asks the provider for
///         <c>CreateQuery&lt;TResult&gt;</c>, which builds a
///         <c>MartenLinqQueryable&lt;TResult&gt;</c>. That constructor calls
///         <see cref="Register{T}" />, where the compiler has already emitted the closed
///         instantiations.
///     </para>
///     <para>
///         The registry is a fast path, not a contract: a miss falls back to the reflective
///         <c>CloseAndBuildAs</c>, which is correct everywhere a JIT is available.
///     </para>
/// </remarks>
internal static class SelectClauseResolvers
{
    private static readonly ConcurrentDictionary<Type, Resolver> _resolvers = new();
    private static readonly ConcurrentDictionary<Type, ScalarResolver> _scalars = new();

    static SelectClauseResolvers()
    {
        // NewScalarSelectClause<T> is constrained to `T : struct`, so Register<T>() below cannot
        // close it -- its own T is only `notnull`. Every scalar type Marten renders natively is
        // known here though, so they are closed up front and a `Select(x => x.Amount)` needs
        // nothing from the consumer. A struct or enum declared in the consumer's own assembly is
        // the one shape left over, and it still falls back to the reflective path.
        RegisterValueType<bool>();
        RegisterValueType<byte>();
        RegisterValueType<sbyte>();
        RegisterValueType<short>();
        RegisterValueType<ushort>();
        RegisterValueType<int>();
        RegisterValueType<uint>();
        RegisterValueType<long>();
        RegisterValueType<ulong>();
        RegisterValueType<float>();
        RegisterValueType<double>();
        RegisterValueType<decimal>();
        RegisterValueType<char>();
        RegisterValueType<Guid>();
        RegisterValueType<DateTime>();
        RegisterValueType<DateTimeOffset>();
        RegisterValueType<DateOnly>();
        RegisterValueType<TimeOnly>();
        RegisterValueType<TimeSpan>();
    }

    /// <summary>
    ///     Record the closed-generic select clauses for <typeparamref name="T" />. Called from the
    ///     generic entry points, where the instantiation is statically reachable.
    /// </summary>
    internal static void Register<T>() where T : notnull
    {
        // ContainsKey first so the steady state is a lookup rather than three delegate allocations.
        if (_resolvers.ContainsKey(typeof(T)))
        {
            return;
        }

        _resolvers[typeof(T)] = new Resolver(
            static (from, selector) => new SelectDataSelectClause<T>(from, selector),
            static from => new DataSelectClause<T>(from),
            static (from, field) => new DataSelectClause<T>(from, field));
    }

    /// <summary>
    ///     Record the closed-generic <c>NewScalarSelectClause&lt;T&gt;</c> for a value type. Called
    ///     from the static constructor for the scalar types Marten renders natively, and available
    ///     to anything else that can name a value type statically.
    /// </summary>
    internal static void RegisterValueType<T>() where T : struct
    {
        if (_scalars.ContainsKey(typeof(T)))
        {
            return;
        }

        _scalars[typeof(T)] = new ScalarResolver(
            static (locator, from) => new NewScalarSelectClause<T>(locator, from),
            static (member, from) => new NewScalarSelectClause<T>(member, from));
    }

    /// <summary>
    ///     A <c>NewScalarSelectClause&lt;scalarType&gt;</c> over a raw locator.
    /// </summary>
    internal static ISelectClause NewScalar(Type scalarType, string locator, string from)
    {
        return _scalars.TryGetValue(scalarType, out var resolver)
            ? resolver.FromLocator(locator, from)
            : typeof(NewScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(locator, from, scalarType);
    }

    /// <summary>
    ///     A <c>NewScalarSelectClause&lt;scalarType&gt;</c> over a member's typed locator.
    /// </summary>
    internal static ISelectClause NewScalar(Type scalarType, IQueryableMember member, string from)
    {
        return _scalars.TryGetValue(scalarType, out var resolver)
            ? resolver.FromMember(member, from)
            : typeof(NewScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(member, from, scalarType);
    }

    /// <summary>
    ///     A <c>SelectDataSelectClause&lt;projectedType&gt;</c>: the object projection rendered as
    ///     <c>jsonb_build_object(...)</c> and deserialized into the projected type.
    /// </summary>
    internal static ISelectClause SelectData(Type projectedType, string from, ISqlFragment selector)
    {
        return _resolvers.TryGetValue(projectedType, out var resolver)
            ? resolver.SelectData(from, selector)
            : typeof(SelectDataSelectClause<>).CloseAndBuildAs<ISelectClause>(from, selector, projectedType);
    }

    /// <summary>
    ///     A <c>DataSelectClause&lt;projectedType&gt;</c> over the default <c>d.data</c> column.
    /// </summary>
    internal static ISelectClause Data(Type projectedType, string from)
    {
        return _resolvers.TryGetValue(projectedType, out var resolver)
            ? resolver.Data(from)
            : typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(from, projectedType);
    }

    /// <summary>
    ///     A <c>DataSelectClause&lt;projectedType&gt;</c> over an explicit field or locator.
    /// </summary>
    internal static ISelectClause Data(Type projectedType, string from, string field)
    {
        return _resolvers.TryGetValue(projectedType, out var resolver)
            ? resolver.DataWithField(from, field)
            : typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(from, field, projectedType);
    }

    private sealed record Resolver(
        Func<string, ISqlFragment, ISelectClause> SelectData,
        Func<string, ISelectClause> Data,
        Func<string, string, ISelectClause> DataWithField);

    private sealed record ScalarResolver(
        Func<string, string, ISelectClause> FromLocator,
        Func<IQueryableMember, string, ISelectClause> FromMember);
}
