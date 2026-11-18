#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Fetching;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Weasel.Storage;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchLivePlan<TDoc, TId>
{
    /// <summary>
    ///     #6086: the events statement of a batched fetch holding a cached aggregate. The floor is the cached version
    ///     when the stream allows it (<see cref="CachedAggregateSql.WriteUsable{TId}" />) and zero when it doesn't,
    ///     decided in this statement, and returned on every row so the read knows which one it got:
    ///     <code>
    ///     with floor as (select case when [usable] then :cached else 0 end as version)
    ///     select [event columns], floor.version
    ///     from floor left outer join mt_events as d on d.stream_id = :id and d.version > floor.version
    ///         and d.is_archived = false [and d.is_skipped = false] [and d.tenant_id = :tenant]
    ///     order by d.seq_id
    ///     </code>
    ///     The filters are the ones every Live event read has (see <c>EventStatement</c>). With no events, the left
    ///     join still returns one row, so the floor always comes back.
    /// </summary>
    /// <remarks>
    ///     The floor and the events come from one statement, so they can't disagree. Deciding from the version
    ///     statement before it could fold a recreated stream's first events onto the old stream's aggregate.
    /// </remarks>
    private void writeCachedEventsStatement(ICommandBuilder builder, TId id, long cachedVersion, IEventStorage selector)
    {
        var conjoined = _events.TenancyStyle == TenancyStyle.Conjoined;
        var streamTenant = conjoined && !IsGlobal ? builder.TenantId : null;
        var eventTenant = conjoined ? IsGlobal ? StorageConstants.DefaultTenantId : builder.TenantId : null;

        builder.Append("with floor as (select case when ");
        CachedAggregateSql.WriteUsable(builder, _events, id, cachedVersion, streamTenant, eventTenant, true);
        builder.Append(" then ");
        builder.AppendParameter(cachedVersion);
        builder.Append(" else 0 end as version) select ");
        builder.Append(_cachedEventFields ??= CachedAggregateSql.EventFields(selector));
        builder.Append(", floor.version from floor left outer join ");
        builder.Append(_events.DatabaseSchemaName);
        builder.Append(".mt_events as d on d.stream_id = ");
        builder.AppendParameter(id);
        builder.Append(" and d.version > floor.version and d.is_archived = false");

        if (_events.EnableEventSkippingInProjectionsOrSubscriptions)
        {
            builder.Append(" and d.is_skipped = false");
        }

        if (eventTenant != null)
        {
            builder.Append(" and d.tenant_id = ");
            builder.AppendParameter(eventTenant);
        }

        builder.Append(" order by d.seq_id");
    }

    private string? _cachedEventFields;

    /// <summary>
    ///     Reads what <see cref="ForWritingQueryHandler" /> asked for with a cached aggregate: the stream version,
    ///     then the events with the floor <see cref="writeCachedEventsStatement" /> chose.
    /// </summary>
    private async Task<IEventStream<TDoc>> readCachedIntoStream(DocumentSessionBase session, TId id,
        CancellationToken cancellation, DbDataReader reader, IEventStorage selector,
        AggregateWriteBaseline<TDoc> baseline, long? expectedStartingVersion)
    {
        try
        {
            long version = 0;
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
            }

            assertExpectedVersion(id, expectedStartingVersion, version);

            await reader.NextResultAsync(cancellation).ConfigureAwait(false);

            var fieldCount = selector.SelectFields().Length;
            var events = new List<IEvent>();
            long floor = 0;
            while (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                floor = await reader.GetFieldValueAsync<long>(fieldCount, cancellation).ConfigureAwait(false);

                // An empty delta still returns its floor, on one row with no event in it
                if (await reader.IsDBNullAsync(0, cancellation).ConfigureAwait(false)) continue;

                events.Add(await ((ISelector<IEvent>)selector).ResolveAsync(reader, cancellation).ConfigureAwait(false));
            }

            // The database turned the cached aggregate down and read the whole stream. It was claimed, so it is
            // gone from the cache either way; the result still goes back after the commit.
            if (floor != baseline.Version)
            {
                baseline = baseline.WithoutAggregate();
            }

            _telemetry.RecordEventsReplayed(events.Count, _aggregateTypeName, OpenTelemetryOptions.LivePlan);

            return await completeStream(session, id, events, baseline, version, expectedStartingVersion, cancellation)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            if (e.InnerException is NpgsqlException { SqlState: PostgresErrorCodes.InFailedSqlTransaction })
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            throw;
        }
    }
}
