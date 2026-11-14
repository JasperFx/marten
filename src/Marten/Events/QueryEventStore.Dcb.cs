#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.Events.Tags;
using Marten.Events.Dcb;
using Marten.Internal.Sessions;
using Marten.Storage;
using Npgsql;

namespace Marten.Events;

internal partial class QueryEventStore
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
        return await QueryByTagsAsync(query, null, cancellation).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<IEvent>> QueryByTagsAsync(EventTagQuery query,
        string[]? aggregatorEventTypeNames, CancellationToken cancellation)
    {
        await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), cancellation).ConfigureAwait(false);

        var storage = (EventDocumentStorage)_session.EventStorage();
        var (sql, paramValues) = BuildTagQuerySql(query, storage.SelectFields(), aggregatorEventTypeNames);
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

    private (string sql, List<object> parameters) BuildTagQuerySql(EventTagQuery query, string[] selectFields,
        string[]? aggregatorEventTypeNames = null)
    {
        var conditions = query.Conditions;
        if (conditions.Count == 0)
        {
            throw new ArgumentException("EventTagQuery must have at least one condition.");
        }

        var schema = _store.Events.DatabaseSchemaName;
        var paramValues = new List<object>();
        var sb = new StringBuilder();
        var isHStore = _store.Events.DcbStorageMode == DcbStorageMode.HStore;

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

        sb.Append(" where ");
        if (isHStore)
        {
            HStoreDcbQueryFragment.AppendOrPredicate(sb, _store.Events, conditions, "e", paramValues);
        }
        else
        {
            // #5897: one index probe per condition, combined on seq_id -- see TagTablesDcbQueryFragment.
            // The LEFT JOINs this replaced also needed #4645's tenant_id join predicate; each probe now
            // carries the tenant filter itself.
            TagTablesDcbQueryFragment.AppendSeqIdPredicate(sb, _store.Events, conditions, "e", _session.TenantId,
                paramValues);
        }

        // Filter by tenant_id for conjoined tenancy
        if (_store.Events.TenancyStyle == TenancyStyle.Conjoined)
        {
            sb.Append(" and e.tenant_id = @p");
            sb.Append(paramValues.Count);
            paramValues.Add(_session.TenantId);
        }

        // #5677: archived events are excluded, as in every other event query
        sb.Append(" and e.is_archived = false");

        // If the aggregator has event type filtering, apply it to limit the returned events
        if (aggregatorEventTypeNames is { Length: > 0 })
        {
            sb.Append(" and e.type = ANY(@p");
            sb.Append(paramValues.Count);
            sb.Append(')');
            paramValues.Add(aggregatorEventTypeNames);
        }

        sb.Append(" order by e.seq_id");

        return (sb.ToString(), paramValues);
    }
}
