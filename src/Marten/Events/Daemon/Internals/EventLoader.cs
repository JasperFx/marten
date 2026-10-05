using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Daemon.Internals;

/// <summary>
/// Adaptive event loader that uses progressively simpler fallback strategies
/// when the primary query times out. This handles the case where projections
/// filter on a small subset of event types with large sequence gaps between
/// matching events.
///
/// Strategy progression on timeout:
/// 1. Normal: seq_id range + type filter (standard query)
/// 2. Skip-ahead: find the first seq_id matching the type filter, then fetch from there
/// 3. Window-step: advance through the sequence in fixed windows until events are found
/// </summary>
internal sealed class EventLoader: IEventLoader
{
    private readonly int _aggregateIndex;
    private readonly int _batchSize;
    private readonly NpgsqlParameter _ceiling;
    private readonly NpgsqlCommand _command;
    private readonly NpgsqlParameter _floor;
    private readonly IEventStorage _storage;
    private readonly DocumentStore _store;
    private readonly ISqlFragment[] _filters;
    private readonly string _schemaName;
    private readonly bool _hasTypeFilter;
    private readonly bool _hasEventTypeIndex;

    /// <summary>
    /// #5277: whether the skip-ahead probe has to join <c>mt_streams</c> at all. Only
    /// <see cref="AggregateTypeFilter"/> emits a predicate against the <c>s</c> alias
    /// ("s.type = ?", see #4744); every other filter Marten builds is on <c>d</c> alone, and
    /// <c>mt_events.stream_id</c> is a foreign key into <c>mt_streams</c>, so for those the join
    /// matches every row and only costs work. Postgres does not eliminate provably-redundant
    /// inner joins on its own.
    /// </summary>
    private readonly bool _skipAheadJoinsStreams;

    // Adaptive strategy state
    private LoadStrategy _currentStrategy = LoadStrategy.Normal;

    private enum LoadStrategy
    {
        Normal,
        SkipAhead,
        WindowStep
    }

    public EventLoader(DocumentStore store, MartenDatabase database, AsyncOptions options, ISqlFragment[] filters)
    {
        _store = store;
        Database = database;
        _filters = filters;

        _storage = (IEventStorage)store.Options.Providers.StorageFor<IEvent>().QueryOnly;
        _batchSize = options.BatchSize;
        _schemaName = store.Options.Events.DatabaseSchemaName;
        _hasTypeFilter = filters.OfType<EventTypeFilter>().Any();
        _hasEventTypeIndex = store.Options.EventGraph.EnableEventTypeIndex;
        _skipAheadJoinsStreams = filters.OfType<AggregateTypeFilter>().Any();

        var builder = new CommandBuilder();
        builder.Append($"select {_storage.SelectFields().Select(x => "d." + x).Join(", ")}, s.type as stream_type");
        builder.Append(
            $" from {_schemaName}.mt_events as d inner join {_schemaName}.mt_streams as s on d.stream_id = s.id");

        if (_store.Options.Events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and d.tenant_id = s.tenant_id");
        }

        var parameters = builder.AppendWithParameters(" where d.seq_id > ? and d.seq_id <= ?");
        _floor = parameters[0];
        _ceiling = parameters[1];
        _floor.NpgsqlDbType = _ceiling.NpgsqlDbType = NpgsqlDbType.Bigint;

        foreach (var filter in filters)
        {
            builder.Append(" and ");
            filter.Apply(builder);
        }

        builder.Append(" order by d.seq_id limit ");
        builder.Append(_batchSize);

        _command = builder.Compile();
        _aggregateIndex = _storage.SelectFields().Length;
    }

    public IMartenDatabase Database { get; }

    public async Task<EventPage> LoadAsync(EventRequest request, CancellationToken token)
    {
        try
        {
            return _currentStrategy switch
            {
                LoadStrategy.SkipAhead => await loadWithSkipAheadAsync(request, token).ConfigureAwait(false),
                LoadStrategy.WindowStep => await loadWithWindowStepAsync(request, token).ConfigureAwait(false),
                _ => await loadNormalAsync(request, token).ConfigureAwait(false)
            };
        }
        // #4720: a deliberate daemon shutdown cancels this token and surfaces as an
        // OperationCanceledException — do not treat that as a query timeout and escalate;
        // let it propagate so the shard stops cleanly.
        catch (Exception ex) when (!token.IsCancellationRequested && isTimeoutException(ex))
        {
            // Only escalate strategy if we have a type filter (otherwise the timeout is from something else).
            // #4720: escalate regardless of whether the (type, seq_id) event-type index is enabled. That
            // composite index cannot serve a multi-type, globally seq_id-ordered LIMIT query, so the normal
            // query can still time out with the index present — the flag now only governs the advisory text.
            if (_hasTypeFilter)
            {
                var nextStrategy = _currentStrategy switch
                {
                    LoadStrategy.Normal => LoadStrategy.SkipAhead,
                    LoadStrategy.SkipAhead => LoadStrategy.WindowStep,
                    _ => LoadStrategy.WindowStep
                };

                if (_hasEventTypeIndex)
                {
                    request.Runtime?.Logger.LogWarning(
                        "Event loading timed out with {Strategy} strategy for range [{Floor}, {Ceiling}]. " +
                        "Falling back to {NextStrategy}.",
                        _currentStrategy, request.Floor, request.HighWater, nextStrategy);
                }
                else
                {
                    request.Runtime?.Logger.LogWarning(
                        "Event loading timed out with {Strategy} strategy for range [{Floor}, {Ceiling}]. " +
                        "Falling back to {NextStrategy}. Consider enabling opts.Events.EnableEventTypeIndex for better performance.",
                        _currentStrategy, request.Floor, request.HighWater, nextStrategy);
                }

                _currentStrategy = nextStrategy;

                // Retry with the next strategy
                return _currentStrategy switch
                {
                    LoadStrategy.SkipAhead => await loadWithSkipAheadAsync(request, token).ConfigureAwait(false),
                    LoadStrategy.WindowStep => await loadWithWindowStepAsync(request, token).ConfigureAwait(false),
                    _ => throw new InvalidOperationException("Unexpected adaptive load strategy")
                };
            }

            throw;
        }
    }

    /// <summary>
    /// Standard query: seq_id range + type filter + ORDER BY seq_id LIMIT batch_size
    /// </summary>
    private async Task<EventPage> loadNormalAsync(EventRequest request, CancellationToken token,
        long? pageFloor = null)
    {
        // #5501: skip-ahead runs its query from an ADJUSTED floor (just before the first matching
        // event) but the page has to keep reporting the floor the caller asked for. JasperFx turns
        // page.Floor into EventRange.SequenceFloor, which is the key of the store's optimistic
        // progression update -- "set last_seq_id = ceiling where last_seq_id = floor". The stored
        // last_seq_id is still the requested floor, so an adjusted floor matches no rows. The
        // window-step loader already keeps the original floor for the same reason.
        var page = new EventPage(pageFloor ?? request.Floor);

        await using var session = (QuerySession)_store.QuerySession(SessionOptions.ForDatabase(Database));
        _floor.Value = request.Floor;
        _ceiling.Value = request.HighWater;

        var skippedEvents = 0;
        var runtime = request.Runtime;

        await using var reader = await session.ExecuteReaderAsync(_command, token).ConfigureAwait(false);
        try
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                try
                {
                    var @event = await _storage.ResolveAsync(reader, token).ConfigureAwait(false);

                    if (!await reader.IsDBNullAsync(_aggregateIndex, token).ConfigureAwait(false))
                    {
                        @event.AggregateTypeName =
                            await reader.GetFieldValueAsync<string>(_aggregateIndex, token).ConfigureAwait(false);
                    }

                    page.Add(@event);
                }
                catch (UnknownEventTypeException e)
                {
                    if (request.ErrorOptions.SkipUnknownEvents)
                    {
                        runtime.Logger.EventUnknown(e.EventTypeName);
                        skippedEvents++;
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (EventDeserializationFailureException e)
                {
                    if (request.ErrorOptions.SkipSerializationErrors)
                    {
                        runtime.Logger.EventDeserializationException(e.InnerException!.GetType().Name!, e.Sequence);
                        runtime.Logger.EventDeserializationExceptionDebug(e);
                        await runtime.RecordDeadLetterEventAsync(e.ToDeadLetterEvent(request.Name)).ConfigureAwait(false);
                        skippedEvents++;
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }
        finally
        {
            await reader.CloseAsync().ConfigureAwait(false);
        }

        page.CalculateCeiling(_batchSize, request.HighWater, skippedEvents);

        // If we got results, reset to normal strategy for next batch
        if (page.Count > 0 && _currentStrategy != LoadStrategy.Normal)
        {
            _currentStrategy = LoadStrategy.Normal;
        }

        return page;
    }

    // #4744 test seam: drive the skip-ahead probe directly so a regression test can prove
    // its SQL is valid (the probe must join mt_streams whenever a filter references the s alias,
    // e.g. AggregateTypeFilter's "s.type = ?") without having to provoke a real statement
    // timeout. Not used at runtime.
    internal Task<EventPage> LoadWithSkipAheadAsync(EventRequest request, CancellationToken token)
        => loadWithSkipAheadAsync(request, token);

    // Exposed for the #5277 SQL-shape regression tests: the probe's SQL is built from
    // StoreOptions alone, so its shape can be asserted with no database round-trip.
    // Not used at runtime.
    internal string SkipAheadCommandText => buildSkipAheadCommand(0).CommandText;

    /// <summary>
    /// #5277: find the first matching seq_id after the floor with an ORDER BY … LIMIT 1 rather than
    /// MIN(d.seq_id). The two return the same sequence, but MIN is an aggregate over the whole
    /// filtered set and Postgres only rewrites it into an ordered index scan when its input is a
    /// single relation — joined to mt_streams it degrades to a bitmap heap scan of every remaining
    /// row in the partition. That made this probe O(events after the floor) on the one code path
    /// that exists precisely BECAUSE the store is too large for the normal query. The explicit
    /// ORDER BY … LIMIT 1 keeps the index scan in every shape, joined or not.
    /// </summary>
    private NpgsqlCommand buildSkipAheadCommand(long floor)
    {
        var builder = new CommandBuilder();
        builder.Append($"select d.seq_id from {_schemaName}.mt_events as d");

        // #4744: the probe replays the same _filters as the normal query, and an AggregateTypeFilter
        // (from a projection or subscription that filters on stream type) emits "s.type = ?" — without
        // the join that predicate references a missing FROM-clause entry and Postgres throws 42P01.
        // #5277: but that is the ONLY filter referencing s, so join only when one is present.
        // mt_events.stream_id is a foreign key into mt_streams, so otherwise the join matches every
        // row and buys nothing.
        if (_skipAheadJoinsStreams)
        {
            builder.Append($" inner join {_schemaName}.mt_streams as s on d.stream_id = s.id");

            if (_store.Options.Events.TenancyStyle == TenancyStyle.Conjoined)
            {
                builder.Append(" and d.tenant_id = s.tenant_id");
            }
        }

        builder.Append(" where d.seq_id > ");
        builder.AppendParameter(floor, NpgsqlDbType.Bigint);

        foreach (var filter in _filters)
        {
            builder.Append(" and ");
            filter.Apply(builder);
        }

        builder.Append(" order by d.seq_id limit 1");

        return builder.Compile();
    }

    /// <summary>
    /// Skip-ahead strategy: find the first seq_id matching the type filter after the floor,
    /// then run the normal query starting from there. Avoids scanning non-matching events.
    /// </summary>
    private async Task<EventPage> loadWithSkipAheadAsync(EventRequest request, CancellationToken token)
    {
        await using var session = (QuerySession)_store.QuerySession(SessionOptions.ForDatabase(Database));

        var probeCommand = buildSkipAheadCommand(request.Floor);
        long? nextMatchingSeqId;

        await using (var reader = await session.ExecuteReaderAsync(probeCommand, token).ConfigureAwait(false))
        {
            // LIMIT 1, so "no row" is the no-match answer. MIN() returned a single NULL row for that
            // case and the old code tested IsDBNull; d.seq_id is NOT NULL, so there is nothing to test.
            nextMatchingSeqId = await reader.ReadAsync(token).ConfigureAwait(false)
                ? reader.GetInt64(0)
                : null;
            await reader.CloseAsync().ConfigureAwait(false);
        }

        if (nextMatchingSeqId == null)
        {
            // No matching events at all — return empty page at high water mark
            var emptyPage = new EventPage(request.Floor);
            emptyPage.CalculateCeiling(_batchSize, request.HighWater, 0);
            return emptyPage;
        }

        // Now load starting just before the found event
        var adjustedRequest = new EventRequest
        {
            Floor = nextMatchingSeqId.Value - 1,
            HighWater = request.HighWater,
            BatchSize = request.BatchSize,
            ErrorOptions = request.ErrorOptions,
            Runtime = request.Runtime,
            Name = request.Name
        };

        _floor.Value = adjustedRequest.Floor;
        _ceiling.Value = adjustedRequest.HighWater;

        // Use the normal loading path with the adjusted floor for the QUERY, but report the page
        // against the floor the caller asked for (#5501).
        return await loadNormalAsync(adjustedRequest, token, request.Floor).ConfigureAwait(false);
    }

    /// <summary>
    /// Window-step strategy: advance through the sequence in fixed windows until
    /// matching events are found. Each window is small enough to avoid timeouts.
    /// </summary>
    private async Task<EventPage> loadWithWindowStepAsync(EventRequest request, CancellationToken token)
    {
        const long windowSize = 10_000;
        var currentFloor = request.Floor;
        var highWater = request.HighWater;

        while (currentFloor < highWater)
        {
            var windowCeiling = Math.Min(currentFloor + windowSize, highWater);

            _floor.Value = currentFloor;
            _ceiling.Value = windowCeiling;

            await using var session = (QuerySession)_store.QuerySession(SessionOptions.ForDatabase(Database));
            var page = new EventPage(request.Floor); // Use original floor for page tracking

            await using var reader = await session.ExecuteReaderAsync(_command, token).ConfigureAwait(false);
            try
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    try
                    {
                        var @event = await _storage.ResolveAsync(reader, token).ConfigureAwait(false);

                        if (!await reader.IsDBNullAsync(_aggregateIndex, token).ConfigureAwait(false))
                        {
                            @event.AggregateTypeName =
                                await reader.GetFieldValueAsync<string>(_aggregateIndex, token).ConfigureAwait(false);
                        }

                        page.Add(@event);
                    }
                    catch (UnknownEventTypeException e)
                    {
                        if (request.ErrorOptions.SkipUnknownEvents)
                        {
                            request.Runtime.Logger.EventUnknown(e.EventTypeName);
                        }
                        else { throw; }
                    }
                    catch (EventDeserializationFailureException e)
                    {
                        if (request.ErrorOptions.SkipSerializationErrors)
                        {
                            request.Runtime.Logger.EventDeserializationException(e.InnerException!.GetType().Name!, e.Sequence);
                            await request.Runtime.RecordDeadLetterEventAsync(e.ToDeadLetterEvent(request.Name)).ConfigureAwait(false);
                        }
                        else { throw; }
                    }
                }
            }
            finally
            {
                await reader.CloseAsync().ConfigureAwait(false);
            }

            if (page.Count > 0)
            {
                page.CalculateCeiling(_batchSize, highWater, 0);
                // Found events — reset strategy for next batch
                _currentStrategy = LoadStrategy.Normal;
                return page;
            }

            // No events in this window — advance
            currentFloor = windowCeiling;
        }

        // Exhausted the entire range with no matching events
        var emptyPage = new EventPage(request.Floor);
        emptyPage.CalculateCeiling(_batchSize, highWater, 0);
        return emptyPage;
    }

    // internal (not private) so the #4720 regression test can assert the chain-walking directly.
    internal static bool isTimeoutException(Exception? ex)
    {
        // #4720: a query timeout does not always arrive as a bare NpgsqlException. Marten's
        // AutoClosingLifetime catches the original exception and re-throws it through
        // MartenExceptionTransformer, which wraps any NpgsqlException into a MartenCommandException
        // (which is NOT an NpgsqlException) before it reaches this catch filter. Inspecting only the
        // outermost exception therefore missed every wrapped timeout and the adaptive fallback never
        // engaged. Walk the whole inner-exception chain instead.
        //
        // Note: the bare TimeoutException arm subsumes the old "NpgsqlException { InnerException:
        // TimeoutException }" case once we walk inner exceptions.
        while (ex is not null)
        {
            if (ex is NpgsqlException { SqlState: "57014" } // query_canceled (statement timeout)
                or TimeoutException
                or OperationCanceledException)
            {
                return true;
            }

            ex = ex.InnerException;
        }

        return false;
    }
}

internal static partial class Log
{
    [LoggerMessage(LogLevel.Warning, "Skipping unknown event type '{EventTypeName}'")]
    public static partial void EventUnknown(this ILogger logger, string eventTypeName);

    [LoggerMessage(LogLevel.Warning,"Suppressed Serialization exception of type {ExceptionName} occured whilst loading event at sequence {Sequence}. Enable debug logging or disable SkipSerializationErrors for full stack trace.")]
    public static partial void EventDeserializationException(this ILogger logger, string exceptionName, long sequence);

    [LoggerMessage(LogLevel.Debug)]
    public static partial void EventDeserializationExceptionDebug(this ILogger logger, Exception exception);
}
