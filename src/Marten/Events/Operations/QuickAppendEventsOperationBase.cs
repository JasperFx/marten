using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Internal;
using Marten.Internal.Operations;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

public abstract class QuickAppendEventsOperationBase : IStorageOperation
{
    public QuickAppendEventsOperationBase(StreamAction stream)
    {
        Stream = stream;
    }

    public StreamAction Stream { get; }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }

    public Type DocumentType => typeof(IEvent);

    public override string ToString()
    {
        return $"Append {Stream.Events.Select(x => x.EventTypeName).Join(", ")} to event stream {Stream}";
    }

    public abstract void ConfigureCommand(ICommandBuilder builder, IMartenSession session);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (reader.Read())
        {
            var values = reader.GetFieldValue<long[]>(0);

            // Ignore the first value
            for (int i = 1; i < values.Length; i++)
            {
                // Only setting the sequence to aid in tombstone processing
                Stream.Events[i - 1].Sequence = values[i];
            }
        }
    }

    protected void writeId(IGroupedParameterBuilder builder)
    {
        var param = builder.AppendParameter(Stream.Id);
        param.NpgsqlDbType = NpgsqlDbType.Uuid;
    }

    protected void writeKey(IGroupedParameterBuilder builder)
    {
        var param = builder.AppendParameter(Stream.Key);
        param.NpgsqlDbType = NpgsqlDbType.Varchar;
    }

    protected void writeBasicParameters(IGroupedParameterBuilder builder, IMartenSession session)
    {
        var param1 = Stream.AggregateTypeName.IsEmpty() ? builder.AppendParameter<object>(DBNull.Value) :  builder.AppendParameter(Stream.AggregateTypeName);
        param1.NpgsqlDbType = NpgsqlDbType.Varchar;

        var param2 = builder.AppendParameter(Stream.TenantId);
        param2.NpgsqlDbType = NpgsqlDbType.Varchar;

        var param3 = builder.AppendParameter(Stream.Events.Select(x => x.Id).ToArray());
        param3.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid;

        var param4 = builder.AppendParameter(Stream.Events.Select(x => x.EventTypeName).ToArray());
        param4.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;

        var param5 = builder.AppendParameter(Stream.Events.Select(x => x.DotNetTypeName).ToArray());
        param5.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;

        var param6 = builder.AppendParameter(Stream.Events.Select(e => session.Serializer.ToJson(e.Data)).ToArray());
        param6.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb;
    }

    protected void writeCausationIds(IGroupedParameterBuilder builder)
    {
        var param = builder.AppendParameter(Stream.Events.Select(x => x.CausationId).ToArray());
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
    }

    protected void writeCorrelationIds(IGroupedParameterBuilder builder)
    {
        var param = builder.AppendParameter(Stream.Events.Select(x => x.CorrelationId).ToArray());
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
    }

    protected void writeHeaders(IGroupedParameterBuilder builder, IMartenSession session)
    {
        var param = builder.AppendParameter(Stream.Events.Select(x => session.Serializer.ToJson(x.Headers)).ToArray());
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb;
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var values = await reader.GetFieldValueAsync<long[]>(0, token).ConfigureAwait(false);

            // Ignore the first value
            for (int i = 1; i < values.Length; i++)
            {
                // Only setting the sequence to aid in tombstone processing
                Stream.Events[i - 1].Sequence = values[i];
            }
        }
    }
}
