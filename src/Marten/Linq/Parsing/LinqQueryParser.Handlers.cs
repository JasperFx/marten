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

namespace Marten.Linq.Parsing;

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

        var nullableSelector = Activator.CreateInstance(typeof(NullableSelector<>).MakeGenericType(typeof(TDocument)), selector);
        var handlerType = typeof(ListQueryHandler<>).MakeGenericType(itemType!);
        return (IQueryHandler<TResult>)Activator.CreateInstance(handlerType, statement, nullableSelector)!;
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

    public IQueryHandler<TResult> BuildHandler<TResult>()
    {
        if (!_collectionUsages.Any())
        {
            var usage = new CollectionUsage(Session.Options, _provider.SourceType);
            _collectionUsages.Insert(0, usage);
        }

        var statements = BuildStatements();

        var handler = buildHandlerForCurrentStatement<TResult>(statements.Top, statements.MainSelector);

        var includes = _collectionUsages.SelectMany(x => x.Includes).ToArray();

        if (includes.Length != 0)
        {
            return new IncludeQueryHandler<TResult>(handler,
                includes.Select(x => x.BuildReader(Session)).ToArray());
        }

        return handler;
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
            var usage = new CollectionUsage(Session.Options, _provider.SourceType);
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
