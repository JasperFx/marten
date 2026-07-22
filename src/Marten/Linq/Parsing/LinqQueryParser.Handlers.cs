using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using JasperFx.Core.Reflection;
using Marten.Linq.Includes;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql.SqlGeneration;
using System.Diagnostics.CodeAnalysis;

using Marten.Exceptions;
using Marten.Internal;

namespace Marten.Linq.Parsing;

[UnconditionalSuppressMessage("Trimming", "IL2067",
    Justification = "Class-level: parameter receives a DAM-annotated Type from a reflective lookup whose source type is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal partial class LinqQueryParser
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IQueryHandler<TResult> BuildHandler<TDocument, TResult>(
        ISelector<TDocument> selector,
        ISqlFragment statement) where TResult : notnull where TDocument : notnull
    {
        if (typeof(TResult).CanBeCastTo<IEnumerable<TDocument>>())
        {
            return (IQueryHandler<TResult>)new ListQueryHandler<TDocument>(statement, selector);
        }

        var itemType = TryGetEnumerableElementType(typeof(TResult));
        var underlying = itemType != null ? Nullable.GetUnderlyingType(itemType) : null;
        if (underlying == null || underlying != typeof(TDocument))
        {
            throw new NotSupportedException("Marten does not know how to use result type " +
                                         typeof(TResult).FullNameInCode());
        }

        // 9.0 (#4308): replace per-call MakeGenericType + Activator.CreateInstance
        // on the LINQ hot path with delegate-cached factories via
        // JasperFx.Core.Reflection.GenericFactoryCache.
        var nullableSelector = GenericFactoryCache.BuildAs<object>(
            typeof(NullableSelector<>),
            typeof(TDocument),
            selector,
            static closed => arg => Activator.CreateInstance(closed, arg)!);

        return (IQueryHandler<TResult>)GenericFactoryCache.BuildAs<object>(
            typeof(ListQueryHandler<>),
            itemType!,
            statement,
            nullableSelector,
            static closed => (a, b) => Activator.CreateInstance(closed, a, b)!);
    }

    private static Type? TryGetEnumerableElementType(Type t)
    {
        if (t.IsArray) return t.GetElementType();
        if (!t.IsGenericType) return null;
        var def = t.GetGenericTypeDefinition();
        if (def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>) ||
            def == typeof(IList<>) || def == typeof(List<>))
            return t.GetGenericArguments()[0];
        return null;
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(bool assertCanStreamRawJson = false)
    {
        if (!_collectionUsages.Any())
        {
            var usage = new CollectionUsage(((IMartenSession)Session).Options, _provider.SourceType);
            _collectionUsages.Insert(0, usage);
        }

        var statements = BuildStatements();

        if (assertCanStreamRawJson)
        {
            AssertCanStreamRawJson(statements.MainSelector);
        }

        var handler = buildHandlerForCurrentStatement<TResult>(statements.Top, statements.MainSelector);

        var includes = _collectionUsages.SelectMany(x => x.Includes).ToArray();

        if (includes.Length != 0)
        {
            return new IncludeQueryHandler<TResult>(handler,
                includes.Select(x => x.BuildReader(Session)).ToArray());
        }

        return handler;
    }

    /// <summary>
    /// GH-5011: raw JSON streaming (StreamJsonArray/StreamOne/StreamMany/StreamJsonFirst/etc.)
    /// copies the bytes of the underlying "data" column directly to the caller without ever
    /// invoking the query's selector. That's only correct when the "data" column actually
    /// holds JSON shaped like the requested result (the stored document itself, or a
    /// server-computed jsonb_build_object() projection). Select() projections that fell back
    /// to a client-side compiled transform select the *source* document's JSON instead, so
    /// streaming them raw would silently return the wrong shape -- refuse clearly instead.
    /// </summary>
    public static void AssertCanStreamRawJson(SelectorStatement selector)
    {
        if (selector.SelectClause is IClientSideProjectionSelectClause)
        {
            throw new BadLinqExpressionException(
                "This Select() projection cannot be streamed as raw JSON because it requires client-side evaluation (method calls, arithmetic, casts, or conditional expressions are not supported). Use ToListAsync() or another non-streaming method instead.");
        }
    }

    private IQueryHandler<TResult> buildHandlerForCurrentStatement<TResult>(Statement top, SelectorStatement selector)
    {
        if (selector.SingleValue)
        {
            return selector.BuildSingleResultHandler<TResult>(Session, top);
        }

        return selector.SelectClause.BuildHandler<TResult>(Session, top, selector);
    }

    public IQueryHandler<IReadOnlyList<T>> BuildListHandler<T>()
    {
        if (!_collectionUsages.Any())
        {
            var usage = new CollectionUsage(((IMartenSession)Session).Options, _provider.SourceType);
            _collectionUsages.Insert(0, usage);
        }

        var statements = BuildStatements();

        var handler =
            statements.MainSelector.SelectClause.BuildHandler<IReadOnlyList<T>>(Session, statements.Top,
                statements.MainSelector);

        var includes = _collectionUsages.SelectMany(x => x.Includes).ToArray();

        if (includes.Length != 0)
        {
            return new IncludeQueryHandler<IReadOnlyList<T>>(handler,
                includes.Select(x => x.BuildReader(Session)).ToArray());
        }

        return handler;
    }
}
