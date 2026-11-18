#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Fetching;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Storage;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchAsyncPlan<TDoc, TId>
{
    /// <summary>
    ///     #6086: the reads of a batched fetch holding a cached aggregate. The same three statements as
    ///     <see cref="writeReads" />, except that the floor is the cached version when the stream allows it
    ///     (<see cref="CachedAggregateSql.WriteUsable{TId}" />), and the snapshot is only loaded when it doesn't:
    ///     <code>
    ///     select version from mt_streams where id = :id [for update];
    ///     with usable as (select [usable] as hit),
    ///          floor as (select case when usable.hit then :cached else [snapshot version] end as version,
    ///                           exists (select 1 from mt_streams as s where s.id = :id) as stream_exists,
    ///                           usable.hit as hit from usable)
    ///     select [event columns], floor.version, floor.stream_exists, floor.hit
    ///     from floor left outer join mt_events as d on d.stream_id = :id and d.version > floor.version
    ///     order by d.seq_id;
    ///     select [snapshot] where id = :id and not [usable]
    ///     </code>
    /// </summary>
    /// <remarks>
    ///     The second statement decides, and says what it decided. The snapshot statement asks again, on a later
    ///     snapshot of the database, only so a hit doesn't load it. If the two disagree, the stream moved from below
    ///     the cached version to above it in between, which means it was deleted and written again; the read then
    ///     finds a floor with no snapshot behind it and throws <see cref="SnapshotRaceException" />, as it does for
    ///     any snapshot that went backwards.
    /// </remarks>
    private void writeCachedReads(ICommandBuilder builder, IStorageSession session, TId id, bool forUpdate,
        LoadByIdHandler<TDoc, TId> loadHandler, long cachedVersion)
    {
        var tenant = _events.TenancyStyle == TenancyStyle.Conjoined && !IsGlobal ? builder.TenantId : null;

        _identityStrategy.BuildCommandForReadingVersionForStream(IsGlobal, builder, id, forUpdate);

        builder.StartNewCommand();

        builder.Append("with usable as (select ");
        CachedAggregateSql.WriteUsable(builder, _events, id, cachedVersion, tenant, tenant, false);
        builder.Append(" as hit), floor as (select case when usable.hit then ");
        builder.AppendParameter(cachedVersion);
        builder.Append(" else coalesce((select a.mt_version from ");
        builder.Append(_storage.TableName.QualifiedName);
        builder.Append(" as a where a.id = ");
        builder.AppendParameter(id);
        appendTenant(builder, "a", tenant);
        builder.Append("), 0) end as version, exists (select 1 from ");
        builder.Append(_events.DatabaseSchemaName);
        builder.Append(".mt_streams as s where s.id = ");
        builder.AppendParameter(id);
        appendTenant(builder, "s", tenant);
        builder.Append(") as stream_exists, usable.hit as hit from usable) select ");
        builder.Append(_selectedEventFields!);
        builder.Append(", floor.version, floor.stream_exists, floor.hit from floor left outer join ");
        builder.Append(_events.DatabaseSchemaName);
        builder.Append(".mt_events as d on d.stream_id = ");
        builder.AppendParameter(id);
        builder.Append(" and d.version > floor.version");
        appendTenant(builder, "d", tenant);
        builder.Append(" order by d.seq_id");

        builder.StartNewCommand();

        loadHandler.ConfigureCommand(builder, session);
        builder.Append(" and not ");
        CachedAggregateSql.WriteUsable(builder, _events, id, cachedVersion, tenant, tenant, false);
    }

    private static void appendTenant(ICommandBuilder builder, string alias, string? tenant)
    {
        if (tenant == null) return;

        builder.Append(" and ");
        builder.Append(alias);
        builder.Append(".tenant_id = ");
        builder.AppendParameter(tenant);
    }

    /// <summary>
    ///     Reads what <see cref="writeCachedReads" /> asked for.
    /// </summary>
    private async Task<IEventStream<TDoc>> readCachedIntoStream(DocumentSessionBase session, TId id,
        CancellationToken cancellation, DbDataReader reader, LoadByIdHandler<TDoc, TId> loadHandler,
        IEventStorage selector, AggregateWriteBaseline<TDoc> baseline, bool detectSnapshotRace,
        long? expectedStartingVersion)
    {
        try
        {
            long version = 0;
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
            }

            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var delta = await readDeltaAsync(reader, selector, cancellation, true).ConfigureAwait(false);

            // The snapshot statement, which is empty on a hit, and is this fetch's last result either way
            await reader.NextResultAsync(cancellation).ConfigureAwait(false);

            TDoc? document;
            IReadOnlyList<IEvent> events;
            if (delta.CacheHit)
            {
                document = baseline.Aggregate;
                events = delta.Events;
                version = reachedVersion(version, false, null, events);
            }
            else
            {
                // The database turned the cached aggregate down and read from the snapshot. It was claimed, so it
                // is gone from the cache either way; the result still goes back after the commit.
                baseline = baseline.WithoutAggregate();

                document = await loadHandler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
                var snapshotVersion = snapshotVersionFor(session, id);
                events = eventsAfterSnapshot(id, document != null, snapshotVersion, delta, detectSnapshotRace);
                version = reachedVersion(version, document != null, snapshotVersion, events);
            }

            return await completeStream(session, id, document, events, version, baseline, expectedStartingVersion,
                cancellation).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            if (e.InnerException is NpgsqlException { SqlState: PostgresErrorCodes.InFailedSqlTransaction })
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(id, e.InnerException!);
            }

            throw;
        }
    }
}
