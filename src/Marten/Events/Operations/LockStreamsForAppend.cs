#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Services;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

/// <summary>
/// #6083: locks the <c>mt_streams</c> row of every stream a unit of work appends to, in one statement and in
/// a stable order (stream id, then tenant), ahead of the appends themselves.
/// </summary>
/// <remarks>
/// <para>
/// Each append locks its own stream row: <c>mt_quick_append_events</c> when it reads the version (#6066), and
/// the Rich path's version UPDATE. A unit of work that appends to several streams runs those appends in the
/// order the session made them, so two sessions appending to the same streams in opposite orders could each
/// hold one row and wait on the other's, and PostgreSQL failed one of them with a deadlock (40P01).
/// </para>
/// <para>
/// Locking every row up front in one order makes the second session wait on the first row instead. The
/// appends then take locks this transaction already holds. The lock is the same one the appends take,
/// FOR NO KEY UPDATE, or FOR UPDATE with <c>UseExclusiveLockOnConcurrentAppends</c>. The appends are not
/// reordered, so events still get their sequence numbers in the order the session appended them.
/// </para>
/// <para>
/// It only runs when a unit of work appends to more than one stream, and it travels in the same batch as the
/// appends, so it adds no round trip. A stream that does not exist yet has no row to lock; its append inserts
/// the row, and a second session creating the same stream waits on that insert as before.
/// </para>
/// </remarks>
internal class LockStreamsForAppend: IStorageOperation
{
    private readonly EventGraph _events;
    private readonly IReadOnlyList<StreamAction> _streams;

    private LockStreamsForAppend(EventGraph events, IReadOnlyList<StreamAction> streams)
    {
        _events = events;
        _streams = streams;
    }

    /// <summary>
    /// Queue the lock when the session appends to more than one stream row. Call it before the appender queues
    /// any append, and after every stream's tenant is final.
    /// </summary>
    public static void QueueIfManyStreams(EventGraph events, DocumentSessionBase session)
    {
        StreamAction? first = null;
        List<StreamAction>? streams = null;

        foreach (var stream in session.WorkTracker.Streams)
        {
            if (stream.Events.Count == 0) continue;

            if (first == null)
            {
                first = stream;
                continue;
            }

            streams ??= [first];
            streams.Add(stream);
        }

        if (streams == null) return;

        var conjoined = events.TenancyStyle == TenancyStyle.Conjoined;
        var distinct = streams
            .DistinctBy(x => (identityOf(events, x), conjoined ? x.TenantId ?? session.TenantId : null))
            .ToArray();

        if (distinct.Length < 2) return;

        foreach (var stream in distinct)
        {
            stream.TenantId ??= session.TenantId;
        }

        session.QueueOperation(new LockStreamsForAppend(events, distinct));
    }

    private static object identityOf(EventGraph events, StreamAction stream)
        => events.StreamIdentity == StreamIdentity.AsGuid ? stream.Id : stream.Key!;

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append("select 1 from ");
        builder.Append(_events.DatabaseSchemaName);
        builder.Append(".");
        builder.Append(StreamsTable.TableName);

        var idType = _events.StreamIdentity == StreamIdentity.AsGuid ? NpgsqlDbType.Uuid : NpgsqlDbType.Varchar;
        object ids = _events.StreamIdentity == StreamIdentity.AsGuid
            ? _streams.Select(x => x.Id).ToArray()
            : _streams.Select(x => x.Key!).ToArray();

        if (_events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" where (id, tenant_id) in (select * from unnest(");
            builder.AppendParameter(ids, NpgsqlDbType.Array | idType);
            builder.Append(", ");
            builder.AppendParameter(_streams.Select(x => x.TenantId!).ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Varchar);
            builder.Append(")) order by id, tenant_id");
        }
        else
        {
            builder.Append(" where id = any(");
            builder.AppendParameter(ids, NpgsqlDbType.Array | idType);
            builder.Append(") order by id");
        }

        builder.Append(_events.UseExclusiveLockOnConcurrentAppends ? " for update" : " for no key update");
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        // The rows are only selected to lock them
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;

    public override string ToString()
    {
        return $"Lock {_streams.Count} streams for append";
    }
}
