#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Events.Dcb;
using Marten.Internal.Sessions;
using Npgsql;

namespace Marten.Events;

internal partial class EventStore
{
    public async Task<bool> EventsExistAsync(EventTagQuery query, CancellationToken cancellation = default)
    {
        await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), cancellation).ConfigureAwait(false);

        var handler = new EventsExistByTagsHandler(_store, query);
        return await _session.ExecuteHandlerAsync(handler, cancellation).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IEvent>> QueryByTagsAsync(EventTagQuery query,
        CancellationToken cancellation = default)
    {
        await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), cancellation).ConfigureAwait(false);

        var storage = (EventDocumentStorage)_session.EventStorage();
        var (sql, paramValues) = BuildTagQuerySql(query, storage.SelectFields());
        var cmd = new NpgsqlCommand(sql);
        for (var i = 0; i < paramValues.Count; i++)
        {
            cmd.Parameters.AddWithValue($"p{i}", paramValues[i]);
        }

        await using var reader = await _session.ExecuteReaderAsync(cmd, cancellation).ConfigureAwait(false);
        return await ReadEventsFromReaderAsync(reader, storage, cancellation).ConfigureAwait(false);
    }

    public async Task<T?> AggregateByTagsAsync<T>(EventTagQuery query,
        CancellationToken cancellation = default) where T : class
    {
        var events = await QueryByTagsAsync(query, cancellation).ConfigureAwait(false);
        if (events.Count == 0) return default;

        return await AggregateEventsAsync<T>(events, cancellation).ConfigureAwait(false);
    }

    public async Task<IEventBoundary<T>> FetchForWritingByTags<T>(EventTagQuery query,
        CancellationToken cancellation = default) where T : class
    {
        var events = await QueryByTagsAsync(query, cancellation).ConfigureAwait(false);
        var lastSeenSequence = events.Count > 0 ? events.Max(e => e.Sequence) : 0;

        T? aggregate = default;
        if (events.Count > 0)
        {
            aggregate = await AggregateEventsAsync<T>(events, cancellation).ConfigureAwait(false);
        }

        // Register the DCB assertion to run at SaveChangesAsync time
        var assertion = new AssertDcbConsistency(_store.Events, query, lastSeenSequence);
        _session.QueueOperation(assertion);

        return new EventBoundary<T>(_session, _store.Events, aggregate, events, lastSeenSequence);
    }

    private async Task<T?> AggregateEventsAsync<T>(IReadOnlyList<IEvent> events,
        CancellationToken cancellation) where T : class
    {
        var aggregator = _store.Options.Projections.AggregatorFor<T>();
        if (aggregator == null)
        {
            throw new InvalidOperationException(
                $"Cannot find an aggregator for type '{typeof(T).Name}'. " +
                "Ensure the type has Apply methods or a registered projection.");
        }

        return await aggregator.BuildAsync(events, _session, default, cancellation)
            .ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<IEvent>> ReadEventsFromReaderAsync(DbDataReader reader,
        EventDocumentStorage storage, CancellationToken cancellation)
    {
        var events = new List<IEvent>();

        while (await reader.ReadAsync(cancellation).ConfigureAwait(false))
        {
            var @event = await storage.ResolveAsync(reader, cancellation).ConfigureAwait(false);
            events.Add(@event);
        }

        return events;
    }

    private (string sql, List<object> parameters) BuildTagQuerySql(EventTagQuery query, string[] selectFields)
    {
        var conditions = query.Conditions;
        if (conditions.Count == 0)
        {
            throw new ArgumentException("EventTagQuery must have at least one condition.");
        }

        var distinctTagTypes = conditions.Select(c => c.TagType).Distinct().ToList();
        var schema = _store.Events.DatabaseSchemaName;
        var paramValues = new List<object>();
        var sb = new StringBuilder();

        // SELECT with explicit columns matching EventDocumentStorage expectations
        sb.Append("select ");
        for (var f = 0; f < selectFields.Length; f++)
        {
            if (f > 0) sb.Append(", ");
            sb.Append("e.");
            sb.Append(selectFields[f]);
        }

        sb.Append(" from ");
        sb.Append(schema);
        sb.Append(".mt_events e");

        // LEFT JOINs to tag tables — an event may only have tags in some of the
        // tag tables, so inner joins would incorrectly filter out events that
        // don't appear in every tag type table.
        for (var i = 0; i < distinctTagTypes.Count; i++)
        {
            var tagType = distinctTagTypes[i];
            var registration = _store.Events.FindTagType(tagType)
                               ?? throw new InvalidOperationException(
                                   $"Tag type '{tagType.Name}' is not registered. Call RegisterTagType<{tagType.Name}>() first.");

            sb.Append(" left join ");
            sb.Append(schema);
            sb.Append(".mt_event_tag_");
            sb.Append(registration.TableSuffix);
            sb.Append(" t");
            sb.Append(i);
            sb.Append(" on e.seq_id = t");
            sb.Append(i);
            sb.Append(".seq_id");
        }

        // WHERE clause with OR conditions
        sb.Append(" where (");
        for (var i = 0; i < conditions.Count; i++)
        {
            if (i > 0) sb.Append(" or ");

            var condition = conditions[i];
            var tagIndex = distinctTagTypes.IndexOf(condition.TagType);

            sb.Append("(t");
            sb.Append(tagIndex);
            sb.Append(".value = @p");
            sb.Append(paramValues.Count);

            var registration = _store.Events.FindTagType(condition.TagType)!;
            var value = registration.ExtractValue(condition.TagValue);
            paramValues.Add(value);

            if (condition.EventType != null)
            {
                sb.Append(" and e.type = @p");
                sb.Append(paramValues.Count);
                var eventTypeName = _store.Events.EventMappingFor(condition.EventType).EventTypeName;
                paramValues.Add(eventTypeName);
            }

            sb.Append(')');
        }

        sb.Append(") order by e.seq_id");

        return (sb.ToString(), paramValues);
    }
}
