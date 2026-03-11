#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Tags;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;

namespace Marten.Events.Dcb;

internal class EventsExistByTagsHandler: IQueryHandler<bool>
{
    private readonly DocumentStore _store;
    private readonly EventTagQuery _query;

    public EventsExistByTagsHandler(DocumentStore store, EventTagQuery query)
    {
        _store = store;
        _query = query;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var conditions = _query.Conditions;
        if (conditions.Count == 0)
        {
            throw new ArgumentException("EventTagQuery must have at least one condition.");
        }

        var distinctTagTypes = conditions.Select(c => c.TagType).Distinct().ToList();
        var schema = _store.Events.DatabaseSchemaName;

        builder.Append("select exists (select 1 from ");

        var first = true;
        for (var i = 0; i < distinctTagTypes.Count; i++)
        {
            var tagType = distinctTagTypes[i];
            var registration = _store.Events.FindTagType(tagType)
                               ?? throw new InvalidOperationException(
                                   $"Tag type '{tagType.Name}' is not registered. Call RegisterTagType<{tagType.Name}>() first.");

            var alias = $"t{i}";
            if (first)
            {
                builder.Append(schema);
                builder.Append(".mt_event_tag_");
                builder.Append(registration.TableSuffix);
                builder.Append(" ");
                builder.Append(alias);
                first = false;
            }
            else
            {
                builder.Append(" inner join ");
                builder.Append(schema);
                builder.Append(".mt_event_tag_");
                builder.Append(registration.TableSuffix);
                builder.Append(" ");
                builder.Append(alias);
                builder.Append(" on t0.seq_id = ");
                builder.Append(alias);
                builder.Append(".seq_id");
            }
        }

        // Join to mt_events only if we need event type filtering
        var hasEventTypeFilter = conditions.Any(c => c.EventType != null);
        if (hasEventTypeFilter)
        {
            builder.Append(" inner join ");
            builder.Append(schema);
            builder.Append(".mt_events e on t0.seq_id = e.seq_id");
        }

        builder.Append(" where (");
        for (var i = 0; i < conditions.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(" or ");
            }

            var condition = conditions[i];
            var tagIndex = distinctTagTypes.IndexOf(condition.TagType);
            var alias = $"t{tagIndex}";

            builder.Append("(");
            builder.Append(alias);
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

        builder.Append(") limit 1)");
    }

    public bool Handle(DbDataReader reader, IMartenSession session)
    {
        return reader.Read() && reader.GetBoolean(0);
    }

    public async Task<bool> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
    {
        return await reader.ReadAsync(token).ConfigureAwait(false) &&
               await reader.GetFieldValueAsync<bool>(0, token).ConfigureAwait(false);
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotSupportedException();
    }
}
