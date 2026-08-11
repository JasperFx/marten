#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Documents;
using Marten.Events;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Linq.CursorPaging;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Services;
using Marten.Util;

namespace Marten.Linq;

internal record WaitForAggregate(TimeSpan Timeout, NonStaleDataTimeoutMode TimeoutMode = NonStaleDataTimeoutMode.ThrowException);

/// <summary>
/// Outcome of a single-document JSON stream that also read the document's <c>mt_version</c>
/// inline. <see cref="Found"/> is false when the query matched no row. At most one of
/// <see cref="Version"/> (Guid optimistic-concurrency mode) or <see cref="Revision"/>
/// (numeric revision mode — projection-target documents and <c>IRevisioned</c>/<c>ILongVersioned</c>
/// types) carries a value; both are null when the document type has no <c>mt_version</c> column
/// (neither metadata flavor enabled) or the value was SQL NULL — in which case no ETag
/// should be emitted.
/// <para>
/// <see cref="BodyWritten"/> is false when the caller's <c>shouldWriteBody</c> predicate declined
/// the payload after seeing the version — the conditional-request (<c>304</c>) case, where nothing
/// was copied into the destination stream.
/// </para>
/// </summary>
internal readonly record struct StreamOneJsonResult(bool Found, Guid? Version, long? Revision, bool BodyWritten);

internal class MartenLinqQueryProvider: IQueryProvider, IDocumentQueryExecutor
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
        foreach (var documentType in parser.DocumentTypes())
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


    #region IDocumentQueryExecutor -- #5216

    // The store-agnostic async terminators behind JasperFx's DocumentQueryableExtensions. They take
    // the queryable rather than closing over one, because the extension methods may have composed a
    // predicate onto it first -- so every one of these reads queryable.Expression rather than any
    // expression this provider was built with.

    public async Task<IReadOnlyList<T>> ExecuteToListAsync<T>(IQueryable<T> queryable, CancellationToken token)
    {
        try
        {
            var parser = new LinqQueryParser(this, _session, queryable.Expression);
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

    public Task<T> ExecuteFirstOrDefaultAsync<T>(IQueryable<T> queryable, CancellationToken token)
    {
        // ExecuteAsync constrains TResult to notnull, and the contract deliberately leaves T
        // unconstrained so a store can return null for "or default". notnull is a nullable-analysis
        // constraint that the CLR does not enforce, so this is a compile-time annotation mismatch
        // and nothing more -- the returned value is exactly what FirstOrDefaultAsync<T> gives today.
#pragma warning disable CS8714
        return ExecuteAsync<T>(queryable.Expression, token, SingleValueMode.FirstOrDefault)!;
#pragma warning restore CS8714
    }

    public Task<int> ExecuteCountAsync<T>(IQueryable<T> queryable, CancellationToken token)
    {
        return ExecuteAsync<int>(queryable.Expression, token, SingleValueMode.Count);
    }

    public Task<bool> ExecuteAnyAsync<T>(IQueryable<T> queryable, CancellationToken token)
    {
        return ExecuteAsync<bool>(queryable.Expression, token, SingleValueMode.Any);
    }

    #endregion

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
    /// The column is read as a Guid when the mapping uses Guid optimistic concurrency, and as a
    /// numeric revision when the mapping uses numeric revisioning (projection-target documents,
    /// <c>IRevisioned</c>/<c>ILongVersioned</c> types) — same physical column, different flavor.
    /// When the document type <typeparamref name="T"/> has no <c>mt_version</c> column (neither
    /// metadata flavor enabled), the column is not appended and the result carries neither a
    /// version nor a revision so the caller emits no ETag.
    /// <para>
    /// <paramref name="shouldWriteBody"/> is consulted after the version is read but before the
    /// document payload is copied into <paramref name="destination"/>, so a conditional-request
    /// caller answering <c>304</c> pays for neither the copy nor the buffer growth. It is not
    /// consulted on the no-version path, where there is nothing to decide on.
    /// </para>
    /// </summary>
    public async Task<StreamOneJsonResult> StreamOneWithVersion<T>(Expression expression, Stream destination,
        Func<Guid?, long?, bool>? shouldWriteBody, CancellationToken token) where T : notnull
    {
        var parser = new LinqQueryParser(this, _session, expression);
        var statements = parser.BuildStatements();

        await EnsureStorageExistsAsync(parser, token).ConfigureAwait(false);

        var statement = statements.Top;
        var main = statements.MainSelector;
        main.Limit = 1;

        // Resolve the mapping from the SOURCE document type, not from T. Under a Select()
        // projection T is the projected type — an anonymous type, a DTO, or a scalar like string —
        // and asking StorageFeatures for a mapping of that either invents one whose version
        // metadata defaults to enabled (so a version column got appended to a projected select) or
        // throws outright for a primitive (#5158). The version being read is the source document's
        // either way, so the source document type is what decides the flavor.
        var documentType = parser.DocumentTypes().FirstOrDefault();
        var mapping = documentType == null
            ? null
            : _session.Options.Storage.FindMapping(documentType) as DocumentMapping;

        // Both flavors keep their value in the same physical mt_version column, so one
        // piggy-backed select serves them; only the CLR type read back differs. For a
        // SingleStreamProjection target the revision *is* the source stream's version, making the
        // resulting ETag byte-for-byte the one StreamAggregate serves for the same stream.
        var numericRevision = mapping is { Metadata.Revision.Enabled: true };

        if (numericRevision || mapping is { Metadata.Version.Enabled: true })
        {
            main.SelectClause = new VersionSelectClause<T>(main.SelectClause);

            var command = statement.BuildCommand(_session);
            var result = await _session
                .StreamOneWithVersion(command, destination, numericRevision, shouldWriteBody, token)
                .ConfigureAwait(false);

            return new StreamOneJsonResult(result.Found, result.Version, result.Revision, result.BodyWritten);
        }

        var plainCommand = statement.BuildCommand(_session);
        var found = await _session.StreamOne(plainCommand, destination, token).ConfigureAwait(false);
        return new StreamOneJsonResult(found, null, null, found);
    }
}
