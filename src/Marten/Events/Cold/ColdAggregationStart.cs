#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Schema;
using Marten.Internal.Sessions;
using Marten.Storage;
using Weasel.Postgresql;

namespace Marten.Events.Cold;

/// <summary>
///     Where an inline or async single-stream aggregation starts when its stored document is missing and the stream
///     has a cold part: the document the cold store kept for it, or else the cold events folded through the
///     projection. Without it the aggregation would fold only the new events, from empty.
/// </summary>
internal static class ColdAggregationStart
{
    /// <summary>
    ///     The start for documents of <typeparamref name="TDoc" />, or null when no cold store is configured or no
    ///     single-stream projection stores them.
    /// </summary>
    public static ColdAggregationStart<TDoc, TId>? For<TDoc, TId>(EventGraph events)
        where TDoc : notnull where TId : notnull
    {
        if (events.ColdEventStore == null) return null;

        if (events.ColdAggregationStarts.TryFind(typeof(TDoc), out var found))
        {
            return found as ColdAggregationStart<TDoc, TId>;
        }

        ColdAggregationStart<TDoc, TId>? start = null;
        if (events.Options.Projections.TryFindAggregate(typeof(TDoc), out var projection)
            && projection.Scope == AggregationScope.SingleStream
            && projection is IAggregator<TDoc, TId, IQuerySession> aggregator)
        {
            start = new ColdAggregationStart<TDoc, TId>(events, aggregator);
        }

        events.ColdAggregationStarts = events.ColdAggregationStarts.AddOrUpdate(typeof(TDoc), start);
        return start;
    }
}

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Folds through a registered projection and the event graph, which are preserved at the registration boundary.")]
internal sealed class ColdAggregationStart<TDoc, TId> where TDoc : notnull where TId : notnull
{
    private readonly EventGraph _events;
    private readonly IColdEventStore _store;
    private readonly IAggregator<TDoc, TId, IQuerySession> _aggregator;
    private readonly bool _isGlobal;

    public ColdAggregationStart(EventGraph events, IAggregator<TDoc, TId, IQuerySession> aggregator)
    {
        _events = events;
        _store = events.ColdEventStore!;
        _aggregator = aggregator;
        _isGlobal = events.GlobalAggregates.Contains(typeof(TDoc));
    }

    /// <summary>
    ///     Adds a starting document to <paramref name="loaded" /> for every identity that has none and whose stream
    ///     has a cold part with nothing warm between it and the events being projected. One query for the batch;
    ///     the cold store is only called for streams that qualify.
    /// </summary>
    public async Task AddMissingAsync(DocumentSessionBase session, TId[] identities, Dictionary<TId, TDoc> loaded,
        Func<TId, string> streamKeyOf, IIdentitySetter<TDoc, TId> identitySetter, CancellationToken token)
    {
        var batch = session.WorkTracker as ProjectionUpdateBatch;

        // A rebuild replays mt_events only; what it means for cold streams is the rebuild's own decision.
        if (batch is { Mode: ShardExecutionMode.Rebuild }) return;

        var missing = new Dictionary<string, TId>();
        foreach (var id in identities)
        {
            if (!loaded.ContainsKey(id)) missing.TryAdd(streamKeyOf(id), id);
        }

        if (missing.Count == 0) return;

        var tenantId = ColdStreamReader.TenantFor(session, _isGlobal);
        var coldVersions = await coldStartsAsync(session, missing.Keys.ToArray(), tenantId, batch?.SequenceFloor, token)
            .ConfigureAwait(false);

        foreach (var (streamKey, coldVersion) in coldVersions)
        {
            var id = missing[streamKey];
            var start = await _store.TryLoadSnapshotAsync<TDoc>(session, streamKey, coldVersion, token)
                .ConfigureAwait(false);

            if (start == null)
            {
                var events = await ColdStreamReader.PrependColdAsync(session, streamKey, tenantId, coldVersion, [],
                    default, token).ConfigureAwait(false);

                if (events.Count > 0)
                {
                    start = await _aggregator.BuildAsync(events, session, default, id, identitySetter, token)
                        .ConfigureAwait(false);
                }
            }

            if (start != null)
            {
                identitySetter.SetIdentity(start, id);
                loaded[id] = start;
            }
        }
    }

    /// <summary>
    ///     The cold version of each stream that has one and no warm event between it and the events being projected.
    ///     A warm event in between was projected already, so the missing document is the projection's own outcome.
    ///     Inline, nothing being projected is in <c>mt_events</c> yet; async, the batch projects the events above
    ///     <paramref name="sequenceFloor" />.
    /// </summary>
    private async Task<List<(string StreamKey, long ColdVersion)>> coldStartsAsync(QuerySession session,
        string[] streamKeys, string tenantId, long? sequenceFloor, CancellationToken token)
    {
        var schema = _events.DatabaseSchemaName;
        var conjoined = _events.TenancyStyle == TenancyStyle.Conjoined;

        var builder = new BatchBuilder { TenantId = tenantId };
        builder.Append($"select s.id, s.{StreamsTable.ColdVersionColumn} from {schema}.{StreamsTable.TableName} as s where s.id = any(");
        builder.AppendParameter(streamKeys);
        builder.Append($") and s.{StreamsTable.ColdVersionColumn} is not null");

        if (conjoined)
        {
            builder.Append(" and s.tenant_id = ");
            builder.AppendParameter(tenantId);
        }

        builder.Append($" and not exists (select 1 from {schema}.mt_events as e where e.stream_id = s.id and e.version > s.{StreamsTable.ColdVersionColumn}");
        if (conjoined) builder.Append(" and e.tenant_id = s.tenant_id");

        if (sequenceFloor.HasValue)
        {
            builder.Append(" and e.seq_id <= ");
            builder.AppendParameter(sequenceFloor.Value);
        }

        builder.Append(')');

        var starts = new List<(string, long)>();
        await using var reader = await session.ExecuteReaderAsync(builder.Compile(), token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            starts.Add((await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false),
                await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false)));
        }

        return starts;
    }
}
