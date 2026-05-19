using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Exceptions;
using JasperFx.Events;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

public abstract class QuickAppendEventsOperationBase : IStorageOperation, IExceptionTransform
{
    // SQLSTATE raised by mt_quick_append_events when the target stream is archived.
    // Keep in sync with QuickAppendEventFunction.WriteCreateStatement.
    private const string ArchivedStreamSqlState = "MT001";

    public bool TryTransform(Exception original, out Exception transformed)
    {
        var pg = original as PostgresException ?? original.InnerException as PostgresException;
        if (pg is { SqlState: ArchivedStreamSqlState })
        {
            transformed = new InvalidStreamOperationException(
                $"Attempted to append event to archived stream with Id '{Stream.Id}'.");
            return true;
        }

        transformed = original;
        return false;
    }

    // 9.0 (#4385): per-batch column-array rentals from ArrayPool<T>.Shared, returned in
    // PostprocessAsync once Npgsql has written the parameters to the wire.
    // Capacity 12 covers writeBasicParameters (4) + writeCausationIds + writeCorrelationIds
    // + writeHeaders + writeUserNames + writeTimestamps + up-to-N tag-type arrays without
    // a List growth. Lazily-allocated so subclasses that override ConfigureCommand without
    // calling any of the helpers pay nothing.
    private List<IDisposable>? _rentals;

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

    private PooledList<T> rentColumn<T>(int count)
    {
        _rentals ??= new List<IDisposable>(12);
        var list = new PooledList<T>(count);
        _rentals.Add(list);
        return list;
    }

    private void releaseColumnRentals()
    {
        if (_rentals is null) return;
        for (var i = 0; i < _rentals.Count; i++) _rentals[i].Dispose();
        _rentals.Clear();
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
        // 9.0 (#4385): rent the parallel column buffers from ArrayPool<T>.Shared via
        // PooledList<T>, which surfaces the user-supplied length via ICollection<T>.Count
        // so Npgsql sends exactly `count` elements (the rented array is typically longer).
        // jsonBodies is byte[][] so each element is serialized directly to UTF-8 via
        // ISerializer.WriteTo emission, skipping the intermediate UTF-16 string
        // materialization that ToJson() would do (#4372 Phase 1).
        var ids = rentColumn<Guid>(count);
        var typeNames = rentColumn<string>(count);
        var dotNetTypeNames = rentColumn<string>(count);
        var jsonBodies = rentColumn<byte[]>(count);

        for (int i = 0; i < count; i++)
        {
            var e = events[i];
            ids[i] = e.Id;
            typeNames[i] = e.EventTypeName;
            dotNetTypeNames[i] = e.DotNetTypeName;
            jsonBodies[i] = SerializeToUtf8(session.Serializer, e.Data);
        }

        var param3 = builder.AppendParameter<IList<Guid>>(ids);
        param3.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid;

        var param4 = builder.AppendParameter<IList<string>>(typeNames);
        param4.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;

        var param5 = builder.AppendParameter<IList<string>>(dotNetTypeNames);
        param5.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;

        var param6 = builder.AppendParameter<IList<byte[]>>(jsonBodies);
        param6.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb;
    }

    /// <summary>
    /// Serialize <paramref name="value"/> to a sized UTF-8 byte[] via the serializer's
    /// <see cref="ISerializer.WriteTo"/> + a pooled staging buffer. Avoids the intermediate
    /// <see cref="string"/> allocation that <see cref="ISerializer.ToJson"/> produces.
    /// </summary>
    private static byte[] SerializeToUtf8(ISerializer serializer, object? value)
    {
        using var buffer = new Services.PooledByteBufferWriter();
        serializer.WriteTo(buffer, value);
        return buffer.ToSizedArray();
    }

    protected void writeCausationIds(IGroupedParameterBuilder builder)
    {
        var events = Stream.Events;
        var count = events.Count;
        var causationIds = rentColumn<string>(count);
        for (int i = 0; i < count; i++) causationIds[i] = events[i].CausationId;

        var param = builder.AppendParameter<IList<string>>(causationIds);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
    }

    protected void writeCorrelationIds(IGroupedParameterBuilder builder)
    {
        var events = Stream.Events;
        var count = events.Count;
        var correlationIds = rentColumn<string>(count);
        for (int i = 0; i < count; i++) correlationIds[i] = events[i].CorrelationId;

        var param = builder.AppendParameter<IList<string>>(correlationIds);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
    }

    protected void writeHeaders(IGroupedParameterBuilder builder, IMartenSession session)
    {
        var events = Stream.Events;
        var count = events.Count;
        var headers = rentColumn<byte[]>(count);
        for (int i = 0; i < count; i++) headers[i] = SerializeToUtf8(session.Serializer, events[i].Headers);

        var param = builder.AppendParameter<IList<byte[]>>(headers);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb;
    }

    protected void writeUserNames(IGroupedParameterBuilder builder, IMartenSession session)
    {
        var events = Stream.Events;
        var count = events.Count;
        var userNames = rentColumn<string>(count);
        for (int i = 0; i < count; i++) userNames[i] = events[i].UserName;

        var param = builder.AppendParameter<IList<string>>(userNames);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
    }

    protected void writeTimestamps(IGroupedParameterBuilder builder)
    {
        var events = Stream.Events;
        var count = events.Count;
        var timestamps = rentColumn<DateTimeOffset>(count);
        for (int i = 0; i < count; i++) timestamps[i] = events[i].Timestamp;

        var param = builder.AppendParameter<IList<DateTimeOffset>>(timestamps);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.TimestampTz;
    }

    protected void writeAllTagValues(IGroupedParameterBuilder builder)
    {
        var tagTypes = Events.TagTypes;
        var events = Stream.Events;
        var count = events.Count;

        foreach (var registration in tagTypes)
        {
            var values = rentColumn<string?>(count);
            for (int i = 0; i < count; i++)
            {
                var tags = events[i].Tags;
                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        if (tag.TagType == registration.TagType)
                        {
                            values[i] = registration.ExtractValue(tag.Value)?.ToString();
                            break;
                        }
                    }
                }
            }

            var param = builder.AppendParameter<IList<string?>>(values);
            param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
        }
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        try
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
        finally
        {
            // Parameter bytes hit the wire before ExecuteReaderAsync returned, so the
            // rentals are safe to release here regardless of whether the body threw.
            releaseColumnRentals();
        }
    }
}
