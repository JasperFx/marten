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
/// 2. Skip-ahead: find MIN(seq_id) matching the type filter, then fetch from there
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
        catch (Exception ex) when (isTimeoutException(ex))
        {
            // Only escalate strategy if we have a type filter (otherwise the timeout is from something else)
            if (_hasTypeFilter && !_hasEventTypeIndex)
            {
                var nextStrategy = _currentStrategy switch
                {
                    LoadStrategy.Normal => LoadStrategy.SkipAhead,
                    LoadStrategy.SkipAhead => LoadStrategy.WindowStep,
                    _ => LoadStrategy.WindowStep
                };

                request.Runtime?.Logger.LogWarning(
                    "Event loading timed out with {Strategy} strategy for range [{Floor}, {Ceiling}]. " +
                    "Falling back to {NextStrategy}. Consider enabling opts.Events.EnableEventTypeIndex for better performance.",
                    _currentStrategy, request.Floor, request.HighWater, nextStrategy);

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
    private async Task<EventPage> loadNormalAsync(EventRequest request, CancellationToken token)
    {
        var page = new EventPage(request.Floor);

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

    /// <summary>
    /// Skip-ahead strategy: find the MIN(seq_id) matching the type filter after the floor,
    /// then run the normal query starting from there. Avoids scanning non-matching events.
    /// </summary>
    private async Task<EventPage> loadWithSkipAheadAsync(EventRequest request, CancellationToken token)
    {
        await using var session = (QuerySession)_store.QuerySession(SessionOptions.ForDatabase(Database));

        // Build a MIN query to find the next matching event
        var minBuilder = new CommandBuilder();
        minBuilder.Append($"select min(seq_id) from {_schemaName}.mt_events as d where d.seq_id > ");
        minBuilder.AppendParameter(request.Floor, NpgsqlDbType.Bigint);

        foreach (var filter in _filters)
        {
            minBuilder.Append(" and ");
            filter.Apply(minBuilder);
        }

        var minCommand = minBuilder.Compile();
        long? nextMatchingSeqId;

        await using (var reader = await session.ExecuteReaderAsync(minCommand, token).ConfigureAwait(false))
        {
            await reader.ReadAsync(token).ConfigureAwait(false);
            nextMatchingSeqId = await reader.IsDBNullAsync(0, token).ConfigureAwait(false)
                ? null : reader.GetInt64(0);
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

        // Use the normal loading path with the adjusted floor
        return await loadNormalAsync(adjustedRequest, token).ConfigureAwait(false);
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

    private static bool isTimeoutException(Exception ex)
    {
        return ex is NpgsqlException { InnerException: TimeoutException }
            or NpgsqlException { SqlState: "57014" } // query_canceled (statement timeout)
            or TimeoutException
            or OperationCanceledException;
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
