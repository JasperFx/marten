using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Dcb;

internal class AssertDcbConsistency: IStorageOperation
{
    private readonly EventGraph _events;
    private readonly EventTagQuery _query;
    private readonly long _lastSeenSequence;

    public AssertDcbConsistency(EventGraph events, EventTagQuery query, long lastSeenSequence)
    {
        _events = events;
        _query = query;
        _lastSeenSequence = lastSeenSequence;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        // Build EXISTS query to check if any new matching events have been appended
        // since our last seen sequence
        builder.Append("select exists (select 1 from ");

        var conditions = _query.Conditions;
        var distinctTagTypes = conditions.Select(c => c.TagType).Distinct().ToList();

        // Start with the first tag table
        var first = true;
        for (var i = 0; i < distinctTagTypes.Count; i++)
        {
            var tagType = distinctTagTypes[i];
            var registration = _events.FindTagType(tagType)
                               ?? throw new InvalidOperationException(
                                   $"Tag type '{tagType.Name}' is not registered. Call RegisterTagType<{tagType.Name}>() first.");

            var alias = $"t{i}";
            if (first)
            {
                builder.Append(_events.DatabaseSchemaName);
                builder.Append(".mt_event_tag_");
                builder.Append(registration.TableSuffix);
                builder.Append(" ");
                builder.Append(alias);
                first = false;
            }
            else
            {
                builder.Append(" inner join ");
                builder.Append(_events.DatabaseSchemaName);
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
            builder.Append(_events.DatabaseSchemaName);
            builder.Append(".mt_events e on t0.seq_id = e.seq_id");
        }

        builder.Append(" where t0.seq_id > ");
        builder.AppendParameter(_lastSeenSequence);

        // Build OR conditions
        builder.Append(" and (");
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

            var registration = _events.FindTagType(condition.TagType)!;
            var value = registration.ExtractValue(condition.TagValue);
            builder.AppendParameter(value);

            if (condition.EventType != null)
            {
                builder.Append(" and e.type = ");
                var eventTypeName = _events.EventMappingFor(condition.EventType).EventTypeName;
                builder.AppendParameter(eventTypeName);
            }

            builder.Append(")");
        }

        builder.Append(") limit 1)");
    }

    public Type DocumentType => typeof(IEvent);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (reader.Read() && reader.GetBoolean(0))
        {
            exceptions.Add(new DcbConcurrencyException(_query, _lastSeenSequence));
        }
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (await reader.ReadAsync(token).ConfigureAwait(false) &&
            await reader.GetFieldValueAsync<bool>(0, token).ConfigureAwait(false))
        {
            exceptions.Add(new DcbConcurrencyException(_query, _lastSeenSequence));
        }
    }

    public OperationRole Role() => OperationRole.Events;
}
