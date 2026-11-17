using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Exceptions;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

public abstract class QuickAppendEventsOperationBase : IStorageOperation, IExceptionTransform, IDisposable
{
    // SQLSTATE raised by mt_quick_append_events when the target stream is archived.
    // Keep in sync with QuickAppendEventFunction.WriteCreateStatement.
    private const string ArchivedStreamSqlState = "MT001";

    // #4614: SQLSTATE raised by mt_quick_append_events when the caller's
    // ExpectedVersionOnServer doesn't match the stream's actual version under
    // UseTenantPartitionedEvents (the bulk path is the only one available there,
    // so it has to enforce optimistic concurrency itself). Translated to
    // EventStreamUnexpectedMaxEventIdException so the user-visible exception
    // matches the rich path's UpdateStreamVersion contract.
    private const string OptimisticVersionMismatchSqlState = "MT003";

    public bool TryTransform(Exception original, out Exception transformed)
    {
        var pg = original as PostgresException ?? original.InnerException as PostgresException;
        if (pg is { SqlState: ArchivedStreamSqlState })
        {
            // marten#5476 / marten#5847: the shared JasperFx.Events type, as Polecat and Fisher throw.
            transformed = new ArchivedStreamException(Stream.Key ?? (object)Stream.Id);
            return true;
        }

        // #6051: an append that creates its stream claims the id in mt_streams_identity, and an id claimed
        // already is refused there whatever the action was. That is a reused stream identity, as it is for Start.
        if (pg is { SqlState: PostgresErrorCodes.UniqueViolation, TableName: StreamIdentityEnforcementTable.TableName })
        {
            transformed = new ExistingStreamIdCollisionException((object)Stream.Key ?? Stream.Id, Stream.AggregateType);
            return true;
        }

        // Two Starts can both pass the function's MT003 check before either commits; the loser's
        // mt_streams insert then raises 23505 instead, and must surface exactly as MT003 would.
        if (pg is { SqlState: OptimisticVersionMismatchSqlState } || IsLostStartRace(pg))
        {
            // #5454: PrepareEvents sets ExpectedVersionOnServer 0 for a Start, so MT003 there means the id is
            // already in use — the condition the dedicated mt_streams INSERT reported as a 23505, surfaced as
            // the same exception type. Non-partitioned only: StartStream_duplicate_id_within_tenant_collides
            // pins the partitioned path's EventStreamUnexpectedMaxEventIdException on purpose.
            if (Stream.ActionType == StreamActionType.Start && Events?.UseTenantPartitionedEvents != true)
            {
                transformed = new ExistingStreamIdCollisionException(
                    (object)Stream.Key ?? Stream.Id, Stream.AggregateType);
                return true;
            }

            transformed = new EventStreamUnexpectedMaxEventIdException(
                Stream.Key ?? (object)Stream.Id,
                Stream.AggregateType,
                expected: Stream.ExpectedVersionOnServer ?? -1,
                actual: ReadActualVersion(pg));
            return true;
        }

        transformed = original;
        return false;
    }

    /// <summary>
    ///     #5914: the stream's version as <c>mt_quick_append_events</c> found it, out of MT003's own message
    ///     (<c>... expected %, actual %</c>). Unlike the rich path's <c>Detail</c>, Npgsql never redacts
    ///     <see cref="PostgresException.MessageText" />, so this needs no <c>Include Error Detail</c>. -1 when the
    ///     message does not carry it: a lost start race (23505) has no version to report.
    /// </summary>
    internal static long ReadActualVersion(PostgresException? pg)
    {
        if (pg is not { SqlState: OptimisticVersionMismatchSqlState, MessageText: { } message })
        {
            return -1;
        }

        const string marker = "actual ";
        var index = message.LastIndexOf(marker, StringComparison.Ordinal);
        return index >= 0 && long.TryParse(message.AsSpan(index + marker.Length).Trim(), out var actual)
            ? actual
            : -1;
    }

    private bool IsLostStartRace(PostgresException? pg)
        => Stream.ActionType == StreamActionType.Start
           && pg is { SqlState: PostgresErrorCodes.UniqueViolation }
           && StreamsTable.IsStreamCollisionTable(pg.TableName);

    // 9.0 (#4385): per-batch column-array rentals from ArrayPool<T>.Shared, returned in
    // Dispose() once the whole unit of work is finished. NOT in PostprocessAsync: the resilience
    // pipeline can re-execute the very same NpgsqlBatch (OperationPage calls ConfigureCommand once,
    // in its constructor, and Compile() only hands back the already-built batch), and a released
    // PooledList reports Count = 0, so the retry would send empty event_ids arrays. See #5262.
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

    public abstract void ConfigureCommand(ICommandBuilder builder, IStorageSession session);

    private PooledList<T> rentColumn<T>(int count)
    {
        var list = new PooledList<T>(count);
        trackRental(list);
        return list;
    }

    private void trackRental(IDisposable rental)
    {
        _rentals ??= new List<IDisposable>(12);
        _rentals.Add(rental);
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

    // #4515 / #4578 Phase 2: placeholder for the data jsonb column when an
    // event is binary-serialized. The real payload travels in the parallel
    // bdata bytea[] array; this keeps the `data NOT NULL` constraint intact
    // without holding the real payload twice.
    private static readonly byte[] s_emptyJsonObjectUtf8 = "{}"u8.ToArray();

    protected void writeBasicParameters(
        IGroupedParameterBuilder builder,
        IStorageSession session,
        Func<IEvent, byte[]?>? serializeEventBdata = null)
    {
        writeBasicParametersCore(builder, session, serializeEventBdata, binarySerializerFor: null);
    }

    /// <summary>
    ///     As <see cref="writeBasicParameters(IGroupedParameterBuilder, IStorageSession, Func{IEvent, byte[]})" />,
    ///     but a binary event's payload is written straight into a pooled buffer through
    ///     <see cref="JasperFx.Events.IEventBinarySerializer.Serialize(Type, object, System.Buffers.IBufferWriter{byte})" />
    ///     and bound to the <c>bdatas bytea[]</c> parameter as a slice of it, with no per-event <c>byte[]</c>
    ///     (#5731, jasperfx#1187).
    /// </summary>
    /// <param name="binarySerializerFor">
    ///     The binary serializer for an event, or <c>null</c> for an event that is stored as JSON.
    /// </param>
    protected void writeBasicParametersWithPooledBdata(
        IGroupedParameterBuilder builder,
        IStorageSession session,
        Func<IEvent, JasperFx.Events.IEventBinarySerializer?> binarySerializerFor)
    {
        ArgumentNullException.ThrowIfNull(binarySerializerFor);
        writeBasicParametersCore(builder, session, serializeEventBdata: null, binarySerializerFor);
    }

    private void writeBasicParametersCore(
        IGroupedParameterBuilder builder,
        IStorageSession session,
        Func<IEvent, byte[]?>? serializeEventBdata,
        Func<IEvent, JasperFx.Events.IEventBinarySerializer?>? binarySerializerFor)
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
        }

        // #4515 Phase 2: the parallel bdata column — bytes for a binary event, NULL for a JSON one. Always
        // bound, even when no binary events are registered, because the mt_quick_append_events function
        // signature unconditionally carries the bdatas bytea[] parameter. Filling it also fills jsonBodies:
        // a binary event gets the {} placeholder there, a JSON event its serialized payload.
        PooledList<ReadOnlyMemory<byte>?>? pooledBdata = null;
        PooledList<byte[]>? bdataArray = null;
        if (binarySerializerFor is not null)
        {
            pooledBdata = writePooledBdata(session, events, jsonBodies, binarySerializerFor);
        }
        else
        {
            bdataArray = rentColumn<byte[]>(count);
            for (int i = 0; i < count; i++)
            {
                var e = events[i];
                var bdataBytes = serializeEventBdata?.Invoke(e);
                if (bdataBytes is not null)
                {
                    // Binary event — placeholder in data, real payload in bdata.
                    jsonBodies[i] = s_emptyJsonObjectUtf8;
                    bdataArray[i] = bdataBytes;
                }
                else
                {
                    // JSON event — payload in data, NULL in bdata.
                    jsonBodies[i] = SerializeToUtf8(session.Serializer, e.Data);
                    bdataArray[i] = null!;
                }
            }
        }

        var param3 = builder.AppendParameter<IList<Guid>>(ids);
        param3.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid;

        var param4 = builder.AppendParameter<IList<string>>(typeNames);
        param4.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;

        var param5 = builder.AppendParameter<IList<string>>(dotNetTypeNames);
        param5.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;

        var param6 = builder.AppendParameter<IList<byte[]>>(jsonBodies);
        param6.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb;

        // bdatas bytea[] — must appear in the call-site parameter sequence in the same position as the
        // function signature declares (right after bodies). The PG function inserts bdatas[index] into
        // mt_events.bdata.
        if (pooledBdata is not null)
        {
            var param7 = builder.AppendParameter<IList<ReadOnlyMemory<byte>?>>(pooledBdata);
            param7.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Bytea;
        }
        else
        {
            var param7 = builder.AppendParameter<IList<byte[]>>(bdataArray!);
            param7.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Bytea;
        }
    }

    /// <summary>
    ///     #5731: every binary event of the batch is serialized into ONE pooled buffer, and the
    ///     <c>bdatas</c> column carries slices of it. The slices are only taken once every event has been
    ///     written, because a write that grows the buffer moves it to a new rented array and returns the
    ///     old one to the pool, so a slice taken earlier would point at memory someone else now owns.
    /// </summary>
    /// <remarks>
    ///     <b>Lifetime.</b> The buffer is a rental of this operation, released with the column arrays in
    ///     <see cref="Dispose" /> once the whole unit of work has finished, never in <c>PostprocessAsync</c>:
    ///     Npgsql copies a parameter's bytes only when the batch executes, and the resilience pipeline can
    ///     execute the same <c>NpgsqlBatch</c> again on a retry (#5262). Releasing it any earlier would send
    ///     another event's bytes, or another library's, to <c>mt_events.bdata</c>.
    /// </remarks>
    private PooledList<ReadOnlyMemory<byte>?> writePooledBdata(
        IStorageSession session,
        IReadOnlyList<IEvent> events,
        PooledList<byte[]> jsonBodies,
        Func<IEvent, JasperFx.Events.IEventBinarySerializer?> binarySerializerFor)
    {
        var count = events.Count;

        // Rented and zeroed (PooledList clears reference-carrying slots), so a JSON event's slot is a
        // null ReadOnlyMemory<byte>?, which Npgsql writes as NULL.
        var bdataArray = rentColumn<ReadOnlyMemory<byte>?>(count);

        PooledByteBufferWriter? buffer = null;
        int[]? ends = null;

        try
        {
            for (int i = 0; i < count; i++)
            {
                var e = events[i];
                var binarySerializer = binarySerializerFor(e);
                if (binarySerializer is null)
                {
                    // JSON event — payload in data, NULL in bdata.
                    jsonBodies[i] = SerializeToUtf8(session.Serializer, e.Data);
                    continue;
                }

                if (buffer is null)
                {
                    buffer = new PooledByteBufferWriter(256);
                    trackRental(buffer);

                    ends = ArrayPool<int>.Shared.Rent(count);
                    ends.AsSpan(0, count).Fill(-1);
                }

                // Binary event — placeholder in data, real payload in bdata.
                binarySerializer.Serialize(e.EventType, e.Data, buffer);
                ends![i] = buffer.WrittenCount;
                jsonBodies[i] = s_emptyJsonObjectUtf8;
            }

            if (buffer is not null)
            {
                var written = buffer.WrittenMemory;
                var start = 0;
                for (int i = 0; i < count; i++)
                {
                    var end = ends![i];
                    if (end < 0) continue;

                    bdataArray[i] = written.Slice(start, end - start);
                    start = end;
                }
            }
        }
        finally
        {
            if (ends is not null) ArrayPool<int>.Shared.Return(ends);
        }

        return bdataArray;
    }

    /// <summary>
    /// Serialize <paramref name="value"/> to a sized UTF-8 byte[] via the serializer's
    /// <see cref="ISerializer.WriteTo"/> + a pooled staging buffer. Avoids the intermediate
    /// <see cref="string"/> allocation that <see cref="ISerializer.ToJson"/> produces.
    /// </summary>
    private static byte[] SerializeToUtf8(IStorageSerializer serializer, object? value)
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

    protected void writeHeaders(IGroupedParameterBuilder builder, IStorageSession session)
    {
        var events = Stream.Events;
        var count = events.Count;
        var headers = rentColumn<byte[]>(count);
        // An event without headers is stored as SQL NULL, as the rich append path and the bulk appender
        // store it. Serializing the null would store the jsonb literal `null`, which every read then hands
        // to the serializer as a Dictionary<string, object> -- a type a source-generated resolver for an
        // AOT app has no reason to know about.
        for (int i = 0; i < count; i++)
        {
            var eventHeaders = events[i].Headers;
            headers[i] = eventHeaders is null ? null! : SerializeToUtf8(session.Serializer, eventHeaders);
        }

        var param = builder.AppendParameter<IList<byte[]>>(headers);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb;
    }

    protected void writeUserNames(IGroupedParameterBuilder builder, IStorageSession session)
    {
        var events = Stream.Events;
        var count = events.Count;
        var userNames = rentColumn<string>(count);
        for (int i = 0; i < count; i++) userNames[i] = events[i].UserName;

        var param = builder.AppendParameter<IList<string>>(userNames);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
    }

    /// <summary>
    /// #4614: bind the caller's <see cref="StreamAction.ExpectedVersionOnServer"/>
    /// to the <c>expected_version</c> parameter of <c>mt_quick_append_events</c>.
    /// Pass null when this stream isn't an optimistic append — the function then
    /// short-circuits the version check entirely.
    /// </summary>
    /// <remarks>
    /// Only called by the partitioned bulk path. The non-partitioned bulk path
    /// is never reached with <c>ExpectedVersionOnServer</c> set — those streams
    /// route through the rich per-event InsertStream + UpdateStreamVersion in
    /// <c>QuickEventAppender.registerOperationsForStreams</c>.
    /// </remarks>
    protected void writeExpectedVersion(IGroupedParameterBuilder builder, bool useBigInt)
    {
        var dbType = useBigInt ? NpgsqlDbType.Bigint : NpgsqlDbType.Integer;
        if (Stream.ExpectedVersionOnServer is { } expected)
        {
            object value = useBigInt ? expected : (object)checked((int)expected);
            var param = builder.AppendParameter(value);
            param.NpgsqlDbType = dbType;
        }
        else
        {
            var param = builder.AppendParameter<object>(DBNull.Value);
            param.NpgsqlDbType = dbType;
        }
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
                // #5569: this is the one column writer that doesn't have a value for every
                // event, so it has to null the slot itself. The buffer comes from
                // ArrayPool<string>.Shared, which is process-wide and hands back whatever the
                // previous renter left behind -- and the pool's contract allows returning
                // without clearing. A leftover string reaches mt_quick_append_events as a
                // non-null tag value, where it is cast to the tag's type: either a 22P02 that
                // fails the whole append, or, if the leftover happens to parse, a silent tag
                // row for an id the event was never tagged with.
                values[i] = null;

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

    /// <summary>
    /// #5896: in <see cref="DcbStorageMode.HStore"/> the bulk function takes the events' tags as one
    /// <c>hstore[]</c> parallel to the events array and writes them in its own insert. Bound in the slot the
    /// TagTables mode's per-type arrays occupy; the two never both apply.
    /// </summary>
    protected bool writesHstoreTags => Events is { DcbStorageMode: DcbStorageMode.HStore, TagTypes.Count: > 0 };

    protected void writeHstoreTagValues(IGroupedParameterBuilder builder)
    {
        var events = Stream.Events;
        var count = events.Count;

        // Null where an event carries no tag of a registered type -- the same rule, and the same refusal of
        // two values of one tag type, as BuildHstore applies everywhere else hstore tags are written.
        var values = new Dictionary<string, string>?[count];
        for (var i = 0; i < count; i++)
        {
            var tags = events[i].Tags;
            if (tags is not { Count: > 0 }) continue;

            var hstore = EventTagOperations.BuildHstore(Events, tags);
            values[i] = hstore.Count == 0 ? null : hstore;
        }

        var param = builder.AppendParameter<IList<Dictionary<string, string>?>>(values);
        param.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Hstore;
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            // #5062: nothing to post-process for an append with no events -- but the row
            // still has to be consumed above so the reader stays aligned for the rest of
            // the page. Bail before touching field 0: on a database whose
            // mt_quick_append_events predates the COALESCE fix, the empty case comes back
            // as ARRAY[NULL] and GetFieldValueAsync<long[]> throws InvalidCastException,
            // which would then replace whatever exception actually made the caller fail.
            if (Stream.Events.Count == 0)
            {
                return;
            }

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

            // UseMandatoryStreamTypeDeclaration rejects appending to a stream that does not
            // exist yet (its first event would come back as version 1). But when
            // UseTenantPartitionedEvents is on, StartStream actions are also routed through this
            // bulk-append operation (see QuickEventAppender.registerOperationsForStreams), and a
            // legitimate StartStream of a brand-new stream likewise produces version 1. Only an
            // Append — not a Start — to a non-existent stream is the case this guard is meant to
            // catch, so exclude StartStream actions here. Without this, a partitioned StartStream
            // throws NonExistentStreamException and the events get tombstoned (#4611).
            if (Events is { UseMandatoryStreamTypeDeclaration: true }
                && events[0].Version == 1
                && Stream.ActionType != StreamActionType.Start)
            {
                throw new NonExistentStreamException(Events.StreamIdentity == StreamIdentity.AsGuid
                    ? Stream.Id
                    : Stream.Key);
            }
        }
    }

    /// <summary>
    /// Returns the rented column buffers. Called once the unit of work is finished, because the
    /// batch these parameters belong to may be executed more than once. See #5262.
    /// </summary>
    public void Dispose()
    {
        releaseColumnRentals();
    }
}
