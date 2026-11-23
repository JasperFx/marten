using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Fetching;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Marten.Services;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Marten.Storage;
using Weasel.Postgresql;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events.Fetching;

/// <summary>
/// An Async snapshot lags its stream until the daemon catches up, so the fetch reads the events after the
/// stored snapshot, then the snapshot, as <see cref="FetchAsyncPlan{TDoc,TId}"/> does, and folds them together.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: as FetchAsyncPlan. Aggregate types are preserved at the projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: as FetchAsyncPlan. AOT consumers pre-generate codegen artifacts.")]
internal class FetchNaturalKeyAsyncPlan<TDoc, TNaturalKey, TId>: FetchNaturalKeyPlan<TDoc, TNaturalKey, TId>
    where TDoc : class where TNaturalKey : notnull where TId : notnull
{
    private readonly IAggregator<TDoc, TId, IQuerySession> _aggregator;
    private readonly IDocumentStorage<TDoc, TId> _storage;
    private readonly string _aggregateTypeName = typeof(TDoc).FullNameInCode();
    private string? _initialSql;
    private string? _selectedEventFields;
    private int _eventFieldCount;

    /// <param name="storage">Storage with <c>DocumentTracking.None</c>, as for <see cref="FetchAsyncPlan{TDoc,TId}"/>:
    /// every fold gets its own document, and the selector records the snapshot's revision, which the snapshot race
    /// detection reads.</param>
    public FetchNaturalKeyAsyncPlan(EventGraph events, NaturalKeyDefinition naturalKey,
        NaturalKeyQueryBuilder lookup, IDocumentStorage<TDoc, TId> storage): base(events, naturalKey, lookup)
    {
        _storage = storage;

        var raw = events.Options.Projections.AggregatorFor<TDoc>();
        _aggregator = raw as IAggregator<TDoc, TId, IQuerySession>
                      ?? typeof(IdentityForwardingAggregator<,,,>)
                          .CloseAndBuildAs<IAggregator<TDoc, TId, IQuerySession>>(raw, _storage, typeof(TDoc),
                              _storage.IdType, typeof(TId), typeof(IQuerySession));
    }

    public override ProjectionLifecycle Lifecycle => ProjectionLifecycle.Async;

    protected override bool UsesWriteCache => true;

    // As FetchAsyncPlan: a snapshot behind the stream is caught up by the delta, one ahead of it is wrong
    protected override bool cachedSnapshotFits(long cachedVersion, long streamVersion) =>
        cachedVersion <= streamVersion;

    // As FetchAsyncPlan.FetchForReading: a stream fetched for writing earlier in the session, plus whatever has
    // been appended to it since
    protected override async ValueTask<(bool found, TDoc? aggregate)> tryFetchFromIdentityMapAsync(
        DocumentSessionBase session, object keyValue, CancellationToken cancellation)
    {
        if (session.ItemMap.TryGetValue(typeof(IEventStream<TDoc>), out var raw) &&
            raw is Dictionary<TId, IEventStream<TDoc>> streams)
        {
            foreach (var (streamId, stream) in streams)
            {
                if (!currentKeyMatches(stream, keyValue)) continue;

                releaseStreamBeforeFolding(session, streamId, stream);

                var aggregate = await _aggregator
                    .BuildAsync(stream.Events, session, stream.Aggregate, streamId, _storage, cancellation)
                    .ConfigureAwait(false);
                return (true, aggregate);
            }
        }

        return (false, null);
    }

    protected override async Task prepareAsync(DocumentSessionBase session, CancellationToken cancellation)
    {
        await base.prepareAsync(session, cancellation).ConfigureAwait(false);
        ensureInitialSql(session.EventStorage());
    }

    private void ensureInitialSql(IEventStorage selector)
    {
        if (_initialSql != null) return;

        var fields = selector.SelectFields();
        _eventFieldCount = fields.Length;
        _selectedEventFields = fields.Select(x => "d." + x).Join(", ");
        _initialSql = $"select {_selectedEventFields} from {_events.DatabaseSchemaName}.mt_events as d";
    }

    protected override void forgetSnapshot(DocumentSessionBase session, TId streamId)
        => FetchAsyncPlan<TDoc, TId>.forgetSnapshot(_storage, session, streamId);

    protected override void writeAggregateStatements(ICommandBuilder builder, QuerySession session,
        object keyValue, CachedSnapshot? cached)
    {
        // A batch enlists without going through prepareAsync
        ensureInitialSql(session.EventStorage());

        builder.StartNewCommand();

        if (cached != null)
        {
            // The baseline version is known, so the delta needs no snapshot read
            builder.Append(_initialSql!);
            builder.Append(" where d.stream_id = ");
            _lookup.WriteStreamIdSubselect(builder, keyValue);
            builder.Append(" and d.version > ");
            builder.AppendParameter(cached.Version);
            writeEventTenancyFilter(builder);
            builder.Append(" order by d.seq_id");
            return;
        }

        // The events first, then the snapshot, as FetchAsyncPlan reads them
        writeEventFetchStatement(builder, keyValue);

        builder.StartNewCommand();
        writeDocumentLoad(builder, _storage, session, keyValue);
    }

    /// <summary>
    /// The events after the snapshot, and the snapshot version they start after (the floor), as
    /// FetchAsyncPlan.writeEventFetchStatement writes them, with the key's stream id subselect in place of the
    /// stream id. With no events, or no stream with the key, the left join still returns one row, so the floor
    /// always comes back.
    /// </summary>
    private void writeEventFetchStatement(ICommandBuilder builder, object keyValue)
    {
        builder.Append("with floor as (select coalesce((select a.mt_version from ");
        builder.Append(_storage.TableName.QualifiedName);
        builder.Append(" as a where a.id = ");
        _lookup.WriteStreamIdSubselect(builder, keyValue);
        if (isConjoined)
        {
            builder.Append(" and a.tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }

        builder.Append("), 0) as version) select ");
        builder.Append(_selectedEventFields!);
        builder.Append(", floor.version from floor left outer join ");
        builder.Append(_events.DatabaseSchemaName);
        builder.Append(".mt_events as d on d.stream_id = ");
        _lookup.WriteStreamIdSubselect(builder, keyValue);
        builder.Append(" and d.version > floor.version");
        writeEventTenancyFilter(builder);
        builder.Append(" order by d.seq_id");
    }

    private bool isConjoined => _events.TenancyStyle == TenancyStyle.Conjoined && !IsGlobal;

    private void writeEventTenancyFilter(ICommandBuilder builder)
    {
        // You must do this for performance even if the stream ids were magically unique across tenants
        if (isConjoined)
        {
            builder.Append(" and d.tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }
    }

    private async Task<FetchAsyncPlan<TDoc, TId>.Delta> readDeltaAsync(DbDataReader reader,
        DocumentSessionBase session, CancellationToken cancellation)
    {
        var selector = (ISelector<IEvent>)session.EventStorage();
        var events = new List<IEvent>();
        long floor = 0;
        while (await reader.ReadAsync(cancellation).ConfigureAwait(false))
        {
            floor = await reader.GetFieldValueAsync<long>(_eventFieldCount, cancellation).ConfigureAwait(false);

            // An empty delta still returns its floor, on one row with no event in it
            if (await reader.IsDBNullAsync(0, cancellation).ConfigureAwait(false)) continue;

            events.Add(await selector.ResolveAsync(reader, cancellation).ConfigureAwait(false));
        }

        // The floor is found through the key's stream id subselect, which joins mt_streams, so a floor always
        // has its stream: a stream that is gone takes its key with it, and the fetch starts a new stream
        return new FetchAsyncPlan<TDoc, TId>.Delta(events, floor, StreamExists: true);
    }

    protected override async Task<(TDoc? aggregate, long version)> readAggregateAsync(DbDataReader reader,
        DocumentSessionBase session, bool found, TId streamId, object? keyValue, long version, bool detectSnapshotRace,
        CachedSnapshot? cached, CancellationToken cancellation)
    {
        TDoc? document;
        IReadOnlyList<IEvent> events;

        await reader.NextResultAsync(cancellation).ConfigureAwait(false);
        if (cached != null)
        {
            document = cached.Aggregate;
            events = await new ListQueryHandler<IEvent>(null, session.EventStorage())
                .HandleAsync(reader, session, cancellation).ConfigureAwait(false);

            // An event appended between the two reads is in the events, so the aggregate is at its version
            version = FetchAsyncPlan<TDoc, TId>.reachedVersion(version, false, null, events);
        }
        else
        {
            // The events after the snapshot, then the snapshot itself
            var delta = await readDeltaAsync(reader, session, cancellation).ConfigureAwait(false);

            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            document = await readDocumentAsync(reader, _storage, session, cancellation).ConfigureAwait(false);

            if (!found) return (default, version);

            // The snapshot's revision is recorded under its own stream, so a snapshot of the stream the key moved
            // to has none under this one. The fetch is redone, as the check after the reads would have it.
            if (document != null && !EqualityComparer<TId>.Default.Equals(_storage.Identity(document), streamId))
            {
                throw new NaturalKeyMovedException(typeof(TDoc), keyValue);
            }

            var snapshotVersion = FetchAsyncPlan<TDoc, TId>.snapshotVersionFor(_storage, session, streamId);
            events = FetchAsyncPlan<TDoc, TId>.eventsAfterSnapshot(streamId, document != null, snapshotVersion,
                delta, detectSnapshotRace);
            version = FetchAsyncPlan<TDoc, TId>.reachedVersion(version, document != null, snapshotVersion, events);
        }

        _events.Options.OpenTelemetry
            .RecordEventsReplayed(events.Count, _aggregateTypeName, OpenTelemetryOptions.AsyncPlan);

        if (events.Any())
        {
            document = await _aggregator.BuildAsync(events, session, document, streamId, _storage, cancellation)
                .ConfigureAwait(false);
        }

        if (document != null)
        {
            _storage.SetIdentity(document, streamId);
        }

        return (document, version);
    }

    protected override async Task<TDoc?> applyPendingEventsAsync(DocumentSessionBase session, TId streamId,
        TDoc? aggregate, IReadOnlyList<IEvent> pending, CancellationToken cancellation)
    {
        aggregate = await _aggregator.BuildAsync(pending, session, aggregate, streamId, _storage, cancellation)
            .ConfigureAwait(false);

        // As FetchAsyncPlan.ProjectLatest: store the updated document so it persists when the session commits
        if (aggregate != null)
        {
            session.Store(aggregate);
        }

        return aggregate;
    }
}
