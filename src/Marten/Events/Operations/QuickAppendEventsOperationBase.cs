using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Exceptions;
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

    public EventGraph Events { get; set; }

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

            var finalVersion = values[0];
            var events = Stream.Events;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                events[i].Version = finalVersion;
                finalVersion--;
            }

            // Ignore the first value
            for (int i = 1; i < values.Length; i++)
            {
                // Only setting the sequence to aid in tombstone processing
                events[i - 1].Sequence = values[i];
            }

            if (Events is { UseMandatoryStreamTypeDeclaration: true } && events[0].Version == 1)
            {
                throw new NonExistentStreamException(Events.StreamIdentity == StreamIdentity.AsGuid
                    ? Stream.Id
                    : Stream.Key);
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

        var events = Stream.Events;
        var count = events.Count;
        var ids = new Guid[count];
        var typeNames = new string[count];
        var dotNetTypeNames = new string[count];
        var jsonBodies = new string[count];

        for (int i = 0; i < count; i++)
        {
            var e = events[i];
            ids[i] = e.Id;
            typeNames[i] = e.EventTypeName;
            dotNetTypeNames[i] = e.DotNetTypeName;
            jsonBodies[i] = session.Serializer.ToJson(e.Data);
        }

        var param3 = builder.AppendParameter(ids);
        param3.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid;

        var param4 = builder.AppendParameter(typeNames);
        param4.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;

        var param5 = builder.AppendParameter(dotNetTypeNames);
        param5.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;

        var param6 = builder.AppendParameter(jsonBodies);
        param6.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb;
    }

    protected void writeCausationIds(IGroupedParameterBuilder builder)
    {
        var events = Stream.Events;
        var count = events.Count;
        var causationIds = new string[count];
        for (int i = 0; i < count; i++) causationIds[i] = events[i].CausationId;

        var param = builder.AppendParameter(causationIds);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
    }

    protected void writeCorrelationIds(IGroupedParameterBuilder builder)
    {
        var events = Stream.Events;
        var count = events.Count;
        var correlationIds = new string[count];
        for (int i = 0; i < count; i++) correlationIds[i] = events[i].CorrelationId;

        var param = builder.AppendParameter(correlationIds);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
    }

    protected void writeHeaders(IGroupedParameterBuilder builder, IMartenSession session)
    {
        var events = Stream.Events;
        var count = events.Count;
        var headers = new string[count];
        for (int i = 0; i < count; i++) headers[i] = session.Serializer.ToJson(events[i].Headers);

        var param = builder.AppendParameter(headers);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb;
    }

    protected void writeUserNames(IGroupedParameterBuilder builder, IMartenSession session)
    {
        var events = Stream.Events;
        var count = events.Count;
        var userNames = new string[count];
        for (int i = 0; i < count; i++) userNames[i] = events[i].UserName;

        var param = builder.AppendParameter(userNames);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
    }

    protected void writeTimestamps(IGroupedParameterBuilder builder)
    {
        var events = Stream.Events;
        var count = events.Count;
        var timestamps = new DateTimeOffset[count];
        for (int i = 0; i < count; i++) timestamps[i] = events[i].Timestamp;

        var param = builder.AppendParameter(timestamps);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.TimestampTz;
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var values = await reader.GetFieldValueAsync<long[]>(0, token).ConfigureAwait(false);

            var finalVersion = values[0];
            var events = Stream.Events;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                events[i].Version = finalVersion;
                finalVersion--;
            }

            // Ignore the first value
            for (int i = 1; i < values.Length; i++)
            {
                // Only setting the sequence to aid in tombstone processing
                events[i - 1].Sequence = values[i];
            }

            if (Events is { UseMandatoryStreamTypeDeclaration: true } && events[0].Version == 1)
            {
                throw new NonExistentStreamException(Events.StreamIdentity == StreamIdentity.AsGuid
                    ? Stream.Id
                    : Stream.Key);
            }
        }
    }
}
