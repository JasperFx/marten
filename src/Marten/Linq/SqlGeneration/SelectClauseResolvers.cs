#nullable enable
using System;
using System.Collections.Concurrent;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
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
///         The <em>scalar</em> half (#5635) cannot be registered from there, because
///         <c>NewScalarSelectClause&lt;T&gt;</c> — and <c>NullableSelector&lt;T&gt;</c> with it — is
///         declared <c>where T : struct</c> while <c>MartenLinqQueryable&lt;T&gt;</c> constrains its
///         own <c>T</c> only to <c>notnull</c>. C# has no way to close a struct-constrained generic
///         from a <c>notnull</c> one without <c>MakeGenericMethod</c>, which is the thing being
///         avoided. So <see cref="RegisterValueType{T}" /> is called from the static constructor for
///         every scalar type Marten renders itself, and from
///         <c>StoreOptions.RegisterScalarType&lt;T&gt;()</c> for an enum or struct out of the
///         consumer's assembly, which Marten cannot name.
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
            static (member, from) => new NewScalarSelectClause<T>(member, from),
            // A list of nullable scalars -- Select(x => x.OptionalAmount) -- resolves the value with
            // the non-nullable selector and wraps it, so both halves have to be rooted here too.
            // CS8714: ListQueryHandler<T> is declared `where T : notnull` and Nullable<T> does not
            // satisfy it in C#. The constraint is erased in metadata, which is exactly why the
            // reflective fallback below can close the same type at runtime.
#pragma warning disable CS8714
            static (statement, inner) =>
                new ListQueryHandler<T?>(statement, new NullableSelector<T>((ISelector<T>)inner)));
#pragma warning restore CS8714
    }

    /// <summary>
    ///     A <c>NewScalarSelectClause&lt;scalarType&gt;</c> over a raw locator.
    /// </summary>
    internal static ISelectClause NewScalar(Type scalarType, string locator, string from)
    {
        return _scalars.TryGetValue(scalarType, out var resolver)
            ? resolver.FromLocator(locator, from)
            : reflectively(scalarType,
                () => typeof(NewScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(locator, from, scalarType));
    }

    /// <summary>
    ///     A <c>NewScalarSelectClause&lt;scalarType&gt;</c> over a member's typed locator.
    /// </summary>
    internal static ISelectClause NewScalar(Type scalarType, IQueryableMember member, string from)
    {
        return _scalars.TryGetValue(scalarType, out var resolver)
            ? resolver.FromMember(member, from)
            : reflectively(scalarType,
                () => typeof(NewScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(member, from, scalarType));
    }

    /// <summary>
    ///     #5635. The reflective fallback for an unregistered value type, which is correct under a JIT
    ///     and has no native code to run under AOT. Name the one line that fixes it rather than
    ///     letting <c>Activator.CreateInstance</c>'s own message stand: it reports a missing
    ///     parameterless constructor on an internal generic, which says nothing about what to do.
    /// </summary>
    private static ISelectClause reflectively(Type scalarType, Func<ISelectClause> build)
    {
        try
        {
            return build();
        }
        catch (MissingMemberException e)
        {
            throw new MartenNotSupportedException(
                $"Marten has no instantiation of its scalar select clause closed over {scalarType.FullNameInCode()}, which a Native AOT image cannot create at runtime. Register the value type once, in the same place as Schema.For<T>():{Environment.NewLine}{Environment.NewLine}"
                + $"    opts.RegisterScalarType<{scalarType.NameInCode()}>();{Environment.NewLine}{Environment.NewLine}"
                + "Marten roots the scalar types it renders itself, so this only applies to an enum or struct from your own assembly. Under a JIT the call does nothing. See https://martendb.io/configuration/aot-publishing and https://github.com/JasperFx/marten/issues/5635.",
                e);
        }
    }

    /// <summary>
    ///     The <c>ListQueryHandler&lt;scalarType?&gt;</c> for a list of nullable scalars, with its
    ///     value resolved by <c>NullableSelector&lt;scalarType&gt;</c>. Returns null when the scalar
    ///     type was never registered, leaving the caller's reflective path to it.
    /// </summary>
    internal static object? TryBuildNullableListHandler(Type scalarType, ISqlFragment? statement, object selector)
    {
        return _scalars.TryGetValue(scalarType, out var resolver)
            ? resolver.NullableListHandler(statement, selector)
            : null;
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
        Func<IQueryableMember, string, ISelectClause> FromMember,
        Func<ISqlFragment?, object, object> NullableListHandler);
}
