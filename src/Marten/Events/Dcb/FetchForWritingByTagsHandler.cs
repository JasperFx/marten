#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.Events.Tags;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Dcb;

internal class FetchForWritingByTagsHandler<T>: IQueryHandler<IEventBoundary<T>> where T : class
{
    private readonly DocumentStore _store;
    private readonly EventTagQuery _query;

    public FetchForWritingByTagsHandler(DocumentStore store, EventTagQuery query)
    {
        _store = store;
        _query = query;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var storage = (EventDocumentStorage)((DocumentSessionBase)session).EventStorage();
        var selectFields = storage.SelectFields();
        var conditions = _query.Conditions;
        var distinctTagTypes = conditions.Select(c => c.TagType).Distinct().ToList();
        var schema = _store.Events.DatabaseSchemaName;

        builder.Append("select ");
        for (var f = 0; f < selectFields.Length; f++)
        {
            if (f > 0) builder.Append(", ");
            builder.Append("e.");
            builder.Append(selectFields[f]);
        }

        builder.Append(" from ");
        builder.Append(schema);
        builder.Append(".mt_events e");

        for (var i = 0; i < distinctTagTypes.Count; i++)
        {
            var tagType = distinctTagTypes[i];
            var registration = _store.Events.FindTagType(tagType)
                               ?? throw new InvalidOperationException(
                                   $"Tag type '{tagType.Name}' is not registered.");

            builder.Append(" left join ");
            builder.Append(schema);
            builder.Append(".mt_event_tag_");
            builder.Append(registration.TableSuffix);
            builder.Append(" t");
            builder.Append(i.ToString());
            builder.Append(" on e.seq_id = t");
            builder.Append(i.ToString());
            builder.Append(".seq_id");
        }

        builder.Append(" where (");
        for (var i = 0; i < conditions.Count; i++)
        {
            if (i > 0) builder.Append(" or ");

            var condition = conditions[i];
            var tagIndex = distinctTagTypes.IndexOf(condition.TagType);

            builder.Append("(t");
            builder.Append(tagIndex.ToString());
            builder.Append(".value = ");

            var registration = _store.Events.FindTagType(condition.TagType)!;
            var value = registration.ExtractValue(condition.TagValue);
            builder.AppendParameter(value);

            if (condition.EventType != null)
            {
                builder.Append(" and e.type = ");
                var eventTypeName = _store.Events.EventMappingFor(condition.EventType).EventTypeName;
                builder.AppendParameter(eventTypeName);
            }

            builder.Append(")");
        }

        builder.Append(")");

        // Filter by tenant_id for conjoined tenancy
        if (_store.Events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and e.tenant_id = ");
            builder.AppendParameter(session.TenantId);
        }

        // If the aggregator has event type filtering, apply it to limit the returned events
        var eventTypeNames = resolveAggregatorEventTypeNames();
        if (eventTypeNames != null)
        {
            builder.Append(" and e.type = ANY(");
            var parameter = builder.AppendParameter(eventTypeNames);
            parameter.NpgsqlDbType = NpgsqlDbType.Varchar | NpgsqlDbType.Array;
            builder.Append(")");
        }

        builder.Append(" order by e.seq_id");
    }

    private string[]? resolveAggregatorEventTypeNames()
    {
        var aggregator = _store.Options.Projections.AggregatorFor<T>();
        if (aggregator is not EventFilterable filterable) return null;

        var includedTypes = filterable.IncludedEventTypes;
        if (includedTypes.Count == 0 || includedTypes.Any(x => x.IsAbstract || x.IsInterface)) return null;

        var additionalAliases = _store.Events.AliasesForEvents(includedTypes);
        return includedTypes
            .Select(x => _store.Events.EventMappingFor(x).Alias)
            .Union(additionalAliases)
            .ToArray();
    }

    public IEventBoundary<T> Handle(DbDataReader reader, IMartenSession session)
    {
        throw new NotSupportedException();
    }

    public async Task<IEventBoundary<T>> HandleAsync(DbDataReader reader, IMartenSession session,
        CancellationToken token)
    {
        var docSession = (DocumentSessionBase)session;
        var storage = (EventDocumentStorage)docSession.EventStorage();

        var events = new List<IEvent>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var @event = await storage.ResolveAsync(reader, token).ConfigureAwait(false);
            events.Add(@event);
        }

        var lastSeenSequence = events.Count > 0 ? events.Max(e => e.Sequence) : 0;

        T? aggregate = default;
        if (events.Count > 0)
        {
            var aggregator = _store.Options.Projections.AggregatorFor<T>();
            if (aggregator == null)
            {
                throw new InvalidOperationException(
                    $"Cannot find an aggregator for type '{typeof(T).Name}'.");
            }

            aggregate = await aggregator.BuildAsync(events, docSession, default, token).ConfigureAwait(false);
        }

        var assertion = new AssertDcbConsistency(_store.Events, _query, lastSeenSequence);
        docSession.QueueOperation(assertion);

        return new EventBoundary<T>(docSession, _store.Events, aggregate, events, lastSeenSequence);
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotSupportedException();
    }
}
