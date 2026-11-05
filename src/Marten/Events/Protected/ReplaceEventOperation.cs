using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Postgresql;

using Marten.Services;

namespace Marten.Events.Protected;

internal class ReplaceEventOperation<T> : IStorageOperation, NoDataReturnedCall where T : class
{
    private readonly EventGraph _graph;
    private readonly T _eventBody;
    private readonly long _sequence;

    public ReplaceEventOperation(EventGraph graph, T eventBody, long sequence)
    {
        _graph = graph;
        _eventBody = eventBody;
        _sequence = sequence;

        Id = CombGuidIdGeneration.NewGuid();
    }

    public Guid Id { get; }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append($"update {_graph.DatabaseSchemaName}.mt_events set ");
        StoredEventPayload.AppendAssignments(builder, session, _graph.EventMappingFor<T>(), _eventBody);

        // marten#5379 / marten#5136: bare now(), never `now() at time zone 'utc'`. The latter strips
        // the offset and the naive result is re-read in the session's TimeZone, so on a non-UTC
        // database a compacted event is re-stamped at the wrong instant. mt_streams.timestamp already
        // used bare now(); this column is now consistent with it.
        builder.Append(", timestamp = now(), id = ");
        builder.AppendParameter(Id);

        if (_graph.MetadataConfig.HeadersEnabled)
        {
            builder.Append(", headers = '{}'");
        }

        if (_graph.MetadataConfig.CausationIdEnabled)
        {
            builder.Append(", causation_id = NULL");
        }

        if (_graph.MetadataConfig.CorrelationIdEnabled)
        {
            builder.Append(", correlation_id = NULL");
        }

        if (_graph.MetadataConfig.UserNameEnabled)
        {
            builder.Append(", user_name = NULL");
        }

        builder.Append(" where seq_id = ");
        builder.AppendParameter(_sequence);

        // #5234: without this the Compacted<T> snapshot -- the calling tenant's whole aggregate
        // state -- is written into every other tenant's same-numbered event.
        builder.AppendConjoinedTenantFilter(session);
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}
