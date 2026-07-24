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
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.CursorPaging;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Caching;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Services;
using Marten.Util;

namespace Marten.Linq;

internal record WaitForAggregate(TimeSpan Timeout, NonStaleDataTimeoutMode TimeoutMode = NonStaleDataTimeoutMode.ThrowException);

/// <summary>
/// Outcome of a single-document JSON stream that also read the document's <c>mt_version</c>
/// inline. <see cref="Found"/> is false when the query matched no row; <see cref="Version"/>
/// is null when the document type has no <c>mt_version</c> column (version metadata disabled)
/// or the value was SQL NULL — in which case no ETag should be emitted.
/// </summary>
internal readonly record struct StreamOneJsonResult(bool Found, Guid? Version);

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

    internal QueryStatistics? Statistics { get; set; }

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
        throw new NotSupportedException(QuerySession.SynchronousNotSupportedMessage);
    }

    public TResult Execute<TResult>(Expression expression)
    {
        throw new NotSupportedException(QuerySession.SynchronousNotSupportedMessage);
    }

    internal async ValueTask EnsureStorageExistsAsync(LinqQueryParser parser,
        CancellationToken cancellationToken)
    {
        await EnsureStorageExistsForTypesAsync(parser.DocumentTypes(), cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask EnsureStorageExistsForTypesAsync(IEnumerable<Type> documentTypes,
        CancellationToken cancellationToken)
    {
        foreach (var documentType in documentTypes)
        {
            await _session.Database.EnsureStorageExistsAsync(documentType, cancellationToken).ConfigureAwait(false);
        }

        if (Waiter != null)
        {
            try
            {
                await _session.Database.WaitForNonStaleProjectionDataAsync(SourceType, Waiter.Timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException) when (Waiter.TimeoutMode == NonStaleDataTimeoutMode.ReturnStaleData)
            {
                // #4749: the caller opted to receive the latest available (possibly stale) data rather
                // than fail when the projection cannot catch up within the timeout — e.g. when a gap in
                // mt_events_sequence left by a failed append makes the high-water mark unreachable. Fall
                // through to execute the query against whatever the projection has materialized so far.
            }
        }
    }

    /// <summary>
    ///     Entry point for the opt-in LINQ query plan cache (see
    ///     <see cref="Marten.Linq.QueryPlanCache" />, https://github.com/JasperFx/marten/issues/5013).
    ///     Attempts to reuse a compiled plan for this expression's structural shape; falls
    ///     back to the normal, uncached execution path whenever the cache is disabled, the
    ///     shape isn't supported, or anything unexpected happens while trying to reuse a
    ///     cached plan.
    /// </summary>
    internal async Task<IReadOnlyList<T>> ExecuteListWithPlanCacheAsync<T>(Expression expression,
        CancellationToken token)
    {
        var cache = _session.Options.Linq.QueryPlanCache;
        if (!cache.Enabled)
        {
            return await ExecuteListAsync<T>(expression, token).ConfigureAwait(false);
        }

        var shape = ExpressionShapeVisitor.Analyze(expression);
        if (!shape.IsSupported)
        {
            return await ExecuteListAsync<T>(expression, token).ConfigureAwait(false);
        }

        var key = shape.BuildKey(SourceType, typeof(T));

        if (cache.TryGet(key, out var cachedPlan))
        {
            var result = await TryExecuteCachedPlanAsync<T>(cachedPlan, shape, token).ConfigureAwait(false);
            if (result != null)
            {
                return result;
            }

            // Something about replaying the cached plan didn't work out (should be rare --
            // e.g. a transient failure). Fall through to the always-correct normal path
            // rather than fail the caller's query.
        }
        else
        {
            var plan = LinqPlanRecorder.TryBuild<IReadOnlyList<T>>(this, _session, expression, shape,
                p => p.BuildListHandler<T>());
            if (plan != null)
            {
                cache.Set(key, plan);
            }
        }

        return await ExecuteListAsync<T>(expression, token).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> ExecuteListAsync<T>(Expression expression, CancellationToken token)
    {
        try
        {
            var parser = new LinqQueryParser(this, _session, expression);
            var handler = parser.BuildListHandler<T>();

            await EnsureStorageExistsAsync(parser, token).ConfigureAwait(false);

            var result = await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
            return result ?? Array.Empty<T>();
        }
        catch (Exception e)
        {
            MartenExceptionTransformer.WrapAndThrow(e);
            throw;
        }
    }

    private async Task<IReadOnlyList<T>?> TryExecuteCachedPlanAsync<T>(CachedLinqPlan plan,
        ExpressionShapeVisitor shape, CancellationToken token)
    {
        try
        {
            var currentValues = new object?[shape.Slots.Count];
            for (var i = 0; i < shape.Slots.Count; i++)
            {
                currentValues[i] = shape.Slots[i].ReduceToConstant().Value;
            }

            await EnsureStorageExistsForTypesAsync(plan.DocumentTypes, token).ConfigureAwait(false);

            var batch = LinqPlanRecorder.RebindValues(plan, currentValues);

            await using var reader = await _session.ExecuteReaderAsync(batch, token).ConfigureAwait(false);
            var handler = (IQueryHandler<IReadOnlyList<T>>)plan.Handler;
            return await handler.HandleAsync(reader, _session, token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Never let a cache-replay failure surface to the caller as an error -- fall
            // back to the normal, always-correct execution path instead.
            return null;
        }
    }


    public async Task<TResult?> ExecuteAsync<TResult>(Expression expression, CancellationToken token,
        SingleValueMode valueMode) where TResult : notnull
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

            var handler = parser.BuildHandler<TResult>(assertCanStreamRawJson: true);

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

    public T? ExecuteHandler<T>(IQueryHandler<T> handler)
    {
        throw new NotSupportedException(QuerySession.SynchronousNotSupportedMessage);
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
        LinqQueryParser.AssertCanStreamRawJson(statements.MainSelector);

        var command = statements.Top.BuildCommand(_session);

        return await _session.StreamMany(command, destination, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute a keyset ("cursor") page: stream the matching documents' raw <c>data</c> JSON
    /// (byte-identical to <see cref="StreamMany"/>) and read the ORDER BY key value(s) of the last
    /// row inline via appended <c>cursor_key_N</c> columns, so the next cursor is built without
    /// hydrating or re-serializing any document. <paramref name="keyColumns"/> are the pre-formatted
    /// <c>&lt;locator&gt; as cursor_key_N</c> SELECT fragments; <paramref name="keyTypes"/> the CLR key
    /// types used to read those columns back. The <paramref name="expression"/> must already carry
    /// the seek <c>Where</c>, the OrderBy chain, and <c>Take(pageSize + 1)</c>.
    /// </summary>
    internal async Task<CursorPageResult> StreamCursorPage(Expression expression, IReadOnlyList<string> keyColumns,
        Type[] keyTypes, int pageSize, CancellationToken token)
    {
        var parser = new LinqQueryParser(this, _session, expression);

        await EnsureStorageExistsAsync(parser, token).ConfigureAwait(false);

        var statements = parser.BuildStatements();
        LinqQueryParser.AssertCanStreamRawJson(statements.MainSelector);

        statements.MainSelector.SelectClause =
            new CursorKeySelectClause(statements.MainSelector.SelectClause, keyColumns);

        var command = statements.Top.BuildCommand(_session);

        await using var reader = await _session.ExecuteReaderAsync(command, token).ConfigureAwait(false);
        var read = await reader.StreamCursorKeyset(keyTypes, pageSize, token).ConfigureAwait(false);

        var nextCursor = read is { HasMore: true, LastKeys: not null }
            ? CursorPagination.EncodeCursor(read.LastKeys)
            : null;

        return new CursorPageResult(read.ItemsJson, read.Count, nextCursor);
    }

    /// <summary>
    /// Stream a single "page" of results plus paging metadata (total item count, page count, etc.)
    /// as a JSON envelope directly to <paramref name="destination"/> in one round trip to the database.
    /// The total row count is retrieved via the same <c>count(*) OVER()</c> mechanism used by
    /// <see cref="QueryStatistics"/> / <see cref="MartenLinqQueryable{T}.Stats"/>.
    /// </summary>
    public async Task<int> StreamPagedMany(Expression expression, Stream destination, int pageNumber, int pageSize,
        QueryStatistics statistics, CancellationToken token)
    {
        var parser = new LinqQueryParser(this, _session, expression);

        await EnsureStorageExistsAsync(parser, token).ConfigureAwait(false);

        var statements = parser.BuildStatements();

        var command = statements.Top.BuildCommand(_session);

        return await _session.StreamPagedMany(command, destination, pageNumber, pageSize, statistics, token).ConfigureAwait(false);
    }

    public async Task<bool> StreamOne(Expression expression, Stream destination, CancellationToken token)
    {
        var parser = new LinqQueryParser(this, _session, expression);
        var statements = parser.BuildStatements();
        LinqQueryParser.AssertCanStreamRawJson(statements.MainSelector);

        await EnsureStorageExistsAsync(parser, token).ConfigureAwait(false);

        var statement = statements.Top;
        statements.MainSelector.Limit = 1;
        var command = statement.BuildCommand(_session);

        return await _session.StreamOne(command, destination, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Streams the first matching document as JSON AND reads its <c>mt_version</c> in the same
    /// round trip — the version column is piggy-backed onto the streaming query via
    /// <see cref="VersionSelectClause{T}"/> (analogous to the <c>count(*) OVER()</c> stats column),
    /// so the ASP.NET Core <c>StreamOne</c> ETag support no longer needs a follow-up metadata query.
    /// When the document type <typeparamref name="T"/> has no <c>mt_version</c> column (version
    /// metadata disabled), the version column is not appended and <see cref="StreamOneJsonResult.Version"/>
    /// comes back null so the caller emits no ETag.
    /// </summary>
    public async Task<StreamOneJsonResult> StreamOneWithVersion<T>(Expression expression, Stream destination,
        CancellationToken token) where T : notnull
    {
        var parser = new LinqQueryParser(this, _session, expression);
        var statements = parser.BuildStatements();

        await EnsureStorageExistsAsync(parser, token).ConfigureAwait(false);

        var statement = statements.Top;
        var main = statements.MainSelector;
        main.Limit = 1;

        var versionEnabled = _session.Options.Storage.FindMapping(typeof(T)) is DocumentMapping
        {
            Metadata.Version.Enabled: true
        };

        if (!versionEnabled)
        {
            var plainCommand = statement.BuildCommand(_session);
            var found = await _session.StreamOne(plainCommand, destination, token).ConfigureAwait(false);
            return new StreamOneJsonResult(found, null);
        }

        main.SelectClause = new VersionSelectClause<T>(main.SelectClause);

        var command = statement.BuildCommand(_session);
        var (streamed, version) = await _session.StreamOneWithVersion(command, destination, token)
            .ConfigureAwait(false);

        return new StreamOneJsonResult(streamed, version);
    }
}

