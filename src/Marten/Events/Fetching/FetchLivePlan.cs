using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Events.Cold;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Npgsql;
using Weasel.Postgresql;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events.Fetching;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal partial class FetchLivePlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class where TId : notnull
{
    private readonly IAggregator<TDoc, TId, IQuerySession> _aggregator;
    private readonly EventGraph _events;
    private readonly bool _readsCold;
    private readonly IIdentitySetter<TDoc, TId> _documentStorage;
    private readonly IEventIdentityStrategy<TId> _identityStrategy;
    private readonly OpenTelemetryOptions _telemetry;
    private readonly string _aggregateTypeName = typeof(TDoc).FullNameInCode();

    public FetchLivePlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy,
        IIdentitySetter<TDoc, TId> documentStorage)
    {
        IsGlobal = events.GlobalAggregates.Contains(typeof(TDoc));

        _events = events;
        _readsCold = events.ColdEventStore != null;
        _identityStrategy = identityStrategy;
        _documentStorage = documentStorage;
        _telemetry = events.Options.OpenTelemetry;

        var raw = events.Options.Projections.AggregatorFor<TDoc>();

        // yeah, I know, this is kind of gross
        if (documentStorage is NulloIdentitySetter<TDoc, TId>)
        {
            _aggregator = (IAggregator<TDoc, TId, IQuerySession>?)raw;
        }
        else
        {
            var simpleType = events.StreamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);
            var idType = documentStorage is IDocumentStorage<TDoc, TId> s ? s.IdType : typeof(TId);

            // The goofy identity forwarding thing is to deal with custom value types. Of course.
            _aggregator = raw as IAggregator<TDoc, TId, IQuerySession>
                          ?? typeof(IdentityForwardingAggregator<,,,>).CloseAndBuildAs<IAggregator<TDoc, TId, IQuerySession>>(raw, _documentStorage, typeof(TDoc), idType, simpleType, typeof(IQuerySession));
        }
    }

    public bool IsGlobal { get; }

    public ProjectionLifecycle Lifecycle => ProjectionLifecycle.Live;

    /// <summary>
    ///     What one read of a stream brought back: the version from <c>mt_streams</c> (when asked for), its
    ///     cold boundary (when a cold store is configured) and the events still in <c>mt_events</c>.
    /// </summary>
    private readonly record struct LiveRead(long Version, long ColdVersion, IReadOnlyList<IEvent> WarmEvents);

    // A cold event store is only accepted with string stream identity, so TId is string whenever it is used
    private static string streamKeyOf(TId id) => (string)(object)id;

    private void configureColdBoundary(ICommandBuilder builder, TId id)
    {
        if (!_readsCold) return;

        ColdStreamReader.WriteBoundaryQuery(_events, builder, streamKeyOf(id),
            IsGlobal ? StorageConstants.DefaultTenantId : builder.TenantId);
        builder.StartNewCommand();
    }

    private async Task<LiveRead> readAsync(DbDataReader reader, IStorageSession session,
        IQueryHandler<IReadOnlyList<IEvent>> handler, bool readsVersion, CancellationToken token)
    {
        long version = 0;
        if (readsVersion)
        {
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            }

            await reader.NextResultAsync(token).ConfigureAwait(false);
        }

        long coldVersion = 0;
        if (_readsCold)
        {
            coldVersion = await ColdStreamReader.ReadBoundaryAsync(reader, token).ConfigureAwait(false);
            await reader.NextResultAsync(token).ConfigureAwait(false);
        }

        var events = await handler.HandleAsync(reader, session, token).ConfigureAwait(false);
        return new LiveRead(version, coldVersion, events);
    }

    /// <summary>
    ///     The events to fold: the cold part first, when the stream has one. Runs after the reader is closed,
    ///     because the cold event store may read through the same session.
    /// </summary>
    private async Task<IReadOnlyList<IEvent>> eventsToFoldAsync(QuerySession session, TId id, LiveRead read,
        CancellationToken token)
    {
        var events = read.WarmEvents;
        if (_readsCold)
        {
            var tenantId = ColdStreamReader.TenantFor(session, IsGlobal);
            var streamKey = streamKeyOf(id);
            if (session is DocumentSessionBase documentSession)
            {
                documentSession.RememberColdVersion(tenantId, streamKey, read.ColdVersion);
            }

            events = await ColdStreamReader.PrependColdAsync(session, streamKey, tenantId,
                read.ColdVersion, read.WarmEvents, default, token).ConfigureAwait(false);
        }

        _telemetry.RecordEventsReplayed(events.Count, _aggregateTypeName, OpenTelemetryOptions.LivePlan);
        return events;
    }

    private async Task<IEventStream<TDoc>> buildStreamAsync(DocumentSessionBase session, TId id, LiveRead read,
        CancellationToken cancellation)
    {
        var events = await eventsToFoldAsync(session, id, read, cancellation).ConfigureAwait(false);

        var document = await _aggregator.BuildAsync(events, session, default, id, _documentStorage, cancellation).ConfigureAwait(false);
        if (document != null)
        {
            _documentStorage.SetIdentity(document, id);
        }

        var stream = read.Version == 0
            ? _identityStrategy.StartStream(document, session, id, cancellation)
            : _identityStrategy.AppendToStream(document, session, id, read.Version, cancellation);

        // This is an optimization for calling FetchForWriting, then immediately calling FetchLatest
        if (((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
        {
            session.StoreDocumentInItemMap(id, stream);
        }

        return stream;
    }

    private static Exception? asStreamLocked(Exception e, TId id)
    {
        if (e.InnerException is NpgsqlException { SqlState: PostgresErrorCodes.InFailedSqlTransaction })
        {
            return new StreamLockedException(id, e.InnerException);
        }

        if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
        {
            return new StreamLockedException(id, e.InnerException);
        }

        return null;
    }
}
