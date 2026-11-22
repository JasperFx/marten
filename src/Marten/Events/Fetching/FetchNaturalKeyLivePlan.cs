using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Internal.Storage;
using Marten.Services;
using JasperFx;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Storage;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events.Fetching;

/// <summary>
/// A Live aggregate has no stored snapshot, so the fetch reads the whole stream, as
/// <see cref="FetchLivePlan{TDoc,TId}"/> does, and aggregates it. With the aggregate write cache, a cached aggregate
/// is caught up by the events after it.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: as FetchLivePlan. Aggregate types are preserved at the projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: as FetchLivePlan. AOT consumers pre-generate codegen artifacts.")]
internal class FetchNaturalKeyLivePlan<TDoc, TNaturalKey, TId>: FetchNaturalKeyPlan<TDoc, TNaturalKey, TId>
    where TDoc : class where TNaturalKey : notnull where TId : notnull
{
    private readonly IAggregator<TDoc, TId, IQuerySession> _aggregator;
    private readonly IIdentitySetter<TDoc, TId> _identitySetter;
    private readonly string _aggregateTypeName = typeof(TDoc).FullNameInCode();

    public FetchNaturalKeyLivePlan(EventGraph events, NaturalKeyDefinition naturalKey, NaturalKeyQueryBuilder lookup,
        IIdentitySetter<TDoc, TId> identitySetter): base(events, naturalKey, lookup)
    {
        _identitySetter = identitySetter;

        var raw = events.Options.Projections.AggregatorFor<TDoc>();

        // The same aggregator resolution as FetchLivePlan
        if (identitySetter is NulloIdentitySetter<TDoc, TId>)
        {
            _aggregator = (IAggregator<TDoc, TId, IQuerySession>)raw;
        }
        else
        {
            var idType = identitySetter is IDocumentStorage<TDoc, TId> s ? s.IdType : typeof(TId);
            _aggregator = raw as IAggregator<TDoc, TId, IQuerySession>
                          ?? typeof(IdentityForwardingAggregator<,,,>)
                              .CloseAndBuildAs<IAggregator<TDoc, TId, IQuerySession>>(raw, _identitySetter,
                                  typeof(TDoc), idType, typeof(TId), typeof(IQuerySession));
        }
    }

    public override ProjectionLifecycle Lifecycle => ProjectionLifecycle.Live;

    protected override async Task prepareAsync(DocumentSessionBase session, CancellationToken cancellation)
    {
        // No document table to ensure for a live aggregate
        await identityStrategy(session).EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
    }

    protected override bool UsesWriteCache => true;

    // As FetchLivePlan: a cached aggregate behind the stream is caught up by the events after it, one ahead of it is
    // wrong
    protected override bool cachedSnapshotFits(long cachedVersion, long streamVersion) =>
        cachedVersion <= streamVersion;

    protected override void writeAggregateStatements(ICommandBuilder builder, QuerySession session,
        object keyValue, CachedSnapshot? cached)
    {
        builder.StartNewCommand();
        eventQuery(session, keyValue, cached?.Version).ConfigureCommand(builder, session);
    }

    // As FetchLivePlan.FetchForReading: a stream fetched for writing earlier in the session, plus whatever has
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
                    .BuildAsync(stream.Events, session, stream.Aggregate, streamId, _identitySetter, cancellation)
                    .ConfigureAwait(false);
                return (true, aggregate);
            }
        }

        return (false, null);
    }

    /// <summary>
    /// The stream's events, or with a cached aggregate only the ones after its version, as FetchLivePlan reads them.
    /// </summary>
    private ListQueryHandler<IEvent> eventQuery(QuerySession session, object keyValue, long? afterVersion)
    {
        var selector = session.EventStorage();
        var statement = new EventStatement(selector, _events)
        {
            TenantId = IsGlobal ? StorageConstants.DefaultTenantId : session.TenantId,
            Filters = afterVersion is { } version
                ? [_lookup.StreamIdFilter(keyValue), new WhereFragment("d.version > ?", version)]
                : [_lookup.StreamIdFilter(keyValue)]
        };

        return new ListQueryHandler<IEvent>(statement, selector);
    }

    protected override async Task<(TDoc? aggregate, long version)> readAggregateAsync(DbDataReader reader, DocumentSessionBase session,
        bool found, TId streamId, object? keyValue, long version, bool detectSnapshotRace, CachedSnapshot? cached,
        CancellationToken cancellation)
    {
        await reader.NextResultAsync(cancellation).ConfigureAwait(false);
        var events = await new ListQueryHandler<IEvent>(null, session.EventStorage())
            .HandleAsync(reader, session, cancellation).ConfigureAwait(false);

        if (!found) return (default, version);

        _events.Options.OpenTelemetry
            .RecordEventsReplayed(events.Count, _aggregateTypeName, OpenTelemetryOptions.LivePlan);

        // With a cached aggregate, the events are the ones after it, and an event appended between the two reads is
        // among them, so the aggregate is at the version of the last one
        if (cached != null)
        {
            version = FetchAsyncPlan<TDoc, TId>.reachedVersion(version, false, null, events);
        }

        var document = await _aggregator.BuildAsync(events, session, cached?.Aggregate, streamId, _identitySetter,
                cancellation)
            .ConfigureAwait(false);

        if (document != null)
        {
            _identitySetter.SetIdentity(document, streamId);
        }

        return (document, version);
    }

    protected override async Task<TDoc?> applyPendingEventsAsync(DocumentSessionBase session, TId streamId,
        TDoc? aggregate, IReadOnlyList<IEvent> pending, CancellationToken cancellation)
    {
        return await _aggregator.BuildAsync(pending, session, aggregate, streamId, _identitySetter, cancellation)
            .ConfigureAwait(false);
    }
}
