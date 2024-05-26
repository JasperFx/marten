#nullable enable
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
    public static IQueryHandler<TResult> BuildHandler<TDocument, TResult>(ISelector<TDocument> selector,
        ISqlFragment statement) where TResult : notnull where TDocument : notnull
    {
        if (typeof(TResult).CanBeCastTo<IEnumerable<TDocument>>())
        {
            return (IQueryHandler<TResult>)new ListQueryHandler<TDocument>(statement, selector);
        }

        throw new NotSupportedException("Marten does not know how to use result type " +
                                        typeof(TResult).FullNameInCode());
    }

    public IQueryHandler<TResult> BuildHandler<TResult>() where TResult : notnull
    {
        if (!_collectionUsages.Any())
        {
            var usage = new CollectionUsage(Session.Options, _provider.SourceType);
            _collectionUsages.Insert(0, usage);
        }

        var statements = BuildStatements();

        var handler = buildHandlerForCurrentStatement<TResult>(statements.Top, statements.MainSelector);

        var includes = _collectionUsages.SelectMany(x => x.Includes).ToArray();

        if (includes.Any())
        {
            return new IncludeQueryHandler<TResult>(handler,
                includes.Select(x => x.BuildReader(Session)).ToArray());
        }

        return handler;
    }

    private IQueryHandler<TResult> buildHandlerForCurrentStatement<TResult>(Statement top, SelectorStatement selector) where TResult : notnull
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

        if (includes.Any())
        {
            return new IncludeQueryHandler<IReadOnlyList<T>>(handler,
                includes.Select(x => x.BuildReader(Session)).ToArray());
        }

        return handler;
    }
}
