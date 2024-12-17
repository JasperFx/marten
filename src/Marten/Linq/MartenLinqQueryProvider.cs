#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Util;

namespace Marten.Linq;

internal record WaitForAggregate(TimeSpan Timeout);

internal class MartenLinqQueryProvider: IQueryProvider
{
    private readonly QuerySession _session;

    public MartenLinqQueryProvider(QuerySession session, Type type)
    {
        _session = session;
        SourceType = type;
    }

    public Type SourceType { get; }

    internal WaitForAggregate? Waiter { get; set; }

    internal QueryStatistics Statistics { get; set; } = null!;

    public IQueryable CreateQuery(Expression expression)
    {
        throw new NotSupportedException();
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        return new MartenLinqQueryable<TElement>(_session, this, expression);
    }

    public object Execute(Expression expression)
    {
        throw new NotSupportedException();
    }

    public TResult Execute<TResult>(Expression expression)
    {
        var parser = new LinqQueryParser(this, _session, expression);
        var handler = parser.BuildHandler<TResult>();

        ensureStorageExists(parser);

        return ExecuteHandler(handler)!;
    }

    private void ensureStorageExists(LinqQueryParser parser)
    {
        foreach (var documentType in parser.DocumentTypes())
        {
            _session.Database.EnsureStorageExists(documentType);
        }
    }

    internal async ValueTask EnsureStorageExistsAsync(LinqQueryParser parser,
        CancellationToken cancellationToken)
    {
        foreach (var documentType in parser.DocumentTypes())
        {
            await _session.Database.EnsureStorageExistsAsync(documentType, cancellationToken).ConfigureAwait(false);
        }

        if (Waiter != null)
        {
            await _session.Database.WaitForNonStaleProjectionDataAsync(SourceType, Waiter.Timeout, cancellationToken).ConfigureAwait(false);
        }
    }


    public async Task<TResult?> ExecuteAsync<TResult>(Expression expression, CancellationToken token,
        SingleValueMode valueMode)
    {
        try
        {
            var parser = new LinqQueryParser(this, _session, expression, valueMode);
            var handler = parser.BuildHandler<TResult>();

            await EnsureStorageExistsAsync(parser, token).ConfigureAwait(false);

            return await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            MartenExceptionTransformer.WrapAndThrow(e);
        }

        return default;
    }

    public async Task<int> StreamJson<TResult>(Stream stream, Expression expression, CancellationToken token,
        SingleValueMode mode) where TResult: notnull
    {
        try
        {
            var parser = new LinqQueryParser(this, _session, expression, mode);

            var handler = parser.BuildHandler<TResult>();

            await EnsureStorageExistsAsync(parser, token).ConfigureAwait(false);

            var cmd = _session.BuildCommand(handler);

            await using var reader = await _session.ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
            return await handler.StreamJson(stream, reader, token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            MartenExceptionTransformer.WrapAndThrow(e);
        }

        return default;
    }

    public async Task<T?> ExecuteHandlerAsync<T>(IQueryHandler<T> handler, CancellationToken token)
    {
        try
        {
            var batch = _session.BuildCommand(handler);

            await using var reader = await _session.ExecuteReaderAsync(batch, token).ConfigureAwait(false);
            return await handler.HandleAsync(reader, _session, token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            MartenExceptionTransformer.WrapAndThrow(e);
        }

        return default;
    }

    [Obsolete(QuerySession.SynchronousRemoval)]
    public T? ExecuteHandler<T>(IQueryHandler<T> handler)
    {
        try
        {
            var cmd = _session.BuildCommand(handler);

            using var reader = _session.ExecuteReader(cmd);
            return handler.Handle(reader, _session);
        }
        catch (Exception e)
        {
            MartenExceptionTransformer.WrapAndThrow(e);
        }

        return default;
    }


    public async IAsyncEnumerable<T> ExecuteAsyncEnumerable<T>(Expression expression,
        MartenLinqQueryProvider martenProvider, [EnumeratorCancellation] CancellationToken token)
    {
        var parser = new LinqQueryParser(this, _session, expression);
        var statements = parser.BuildStatements();

        await EnsureStorageExistsAsync(parser, token).ConfigureAwait(false);

        var selector = (ISelector<T>)statements.MainSelector.SelectClause.BuildSelector(_session);
        var statement = statements.Top;

        var cmd = _session.BuildCommand(statement);

        await using var reader = await _session.ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
        var totalRowsColumnIndex = martenProvider.Statistics != null ? reader.GetOrdinal("total_rows") : -1;
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (martenProvider.Statistics != null)
            {
                martenProvider.Statistics.TotalResults = await reader.GetFieldValueAsync<int>(totalRowsColumnIndex, token).ConfigureAwait(false);
            }
            yield return await selector.ResolveAsync(reader, token).ConfigureAwait(false);
        }
    }

    public async Task<int> StreamMany(Expression expression, Stream destination, CancellationToken token)
    {
        var parser = new LinqQueryParser(this, _session, expression);

        await EnsureStorageExistsAsync(parser, token).ConfigureAwait(false);

        var statements = parser.BuildStatements();

        var command = statements.Top.BuildCommand(_session);

        return await _session.StreamMany(command, destination, token).ConfigureAwait(false);
    }

    public async Task<bool> StreamOne(Expression expression, Stream destination, CancellationToken token)
    {
        var parser = new LinqQueryParser(this, _session, expression);
        var statements = parser.BuildStatements();

        await EnsureStorageExistsAsync(parser, token).ConfigureAwait(false);

        var statement = statements.Top;
        statements.MainSelector.Limit = 1;
        var command = statement.BuildCommand(_session);

        return await _session.StreamOne(command, destination, token).ConfigureAwait(false);
    }
}
