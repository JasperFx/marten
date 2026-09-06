using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.ComplianceTests;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.Events.Tags;
using Marten.Events;
using Marten.Services.BatchQuerying;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Marten.Testing.Harness;

/// <summary>
/// Marten's implementation of the cross-store event sourcing compliance seam, closing it over
/// Marten's <c>IEventStore&lt;IDocumentOperations, IQuerySession&gt;</c> session pair.
/// </summary>
public class MartenComplianceFixture: EventStoreComplianceFixture<IDocumentOperations, IQuerySession>
{
    private readonly List<object> _disposables = new();
    private DocumentStore _store = null!;

    public DocumentStore Store => _store;

    protected override async Task BuildStoreAsync(ComplianceStoreConfig config)
    {
        var options = new StoreOptions();
        options.Connection(connectionStringFor(config));
        options.AutoCreateSchemaObjects = AutoCreate.All;
        options.DisableNpgsqlLogging = true;
        options.NameDataLength = 100;
        options.DatabaseSchemaName = (config.SchemaName ?? "compliance").ToLowerInvariant();

        if (config.MaxConcurrentRebuildsPerDatabase.HasValue)
        {
            options.Projections.MaxConcurrentRebuildsPerDatabase = config.MaxConcurrentRebuildsPerDatabase;
        }

        if (config.StreamIdentity.HasValue)
        {
            options.Events.StreamIdentity = config.StreamIdentity.Value;
        }

        if (config.EnableCorrelationTracking)
        {
            options.Events.MetadataConfig.CorrelationIdEnabled = true;
            options.Events.MetadataConfig.CausationIdEnabled = true;
        }

        // Opt-in exactly like correlation tracking above -- the user_name column is only
        // captured (and only queryable) when the store enables it. Added for the jasperfx#737
        // EventQueryCompliance suite's user-name filter facts.
        if (config.EnableUserNameTracking)
        {
            options.Events.MetadataConfig.UserNameEnabled = true;
        }

        if (config.EnableHeaders)
        {
            options.Events.MetadataConfig.HeadersEnabled = true;
        }

        if (config.ConjoinedEventTenancy)
        {
            options.Events.TenancyStyle = JasperFx.MultiTenancy.TenancyStyle.Conjoined;

            // #5343: conjoined EVENTS are not enough on their own once the configuration also
            // registers a snapshot. Marten refuses to build a store whose events are Conjoined but
            // whose projected aggregate document is Single ("Tenancy storage style mismatch"), and
            // that is Marten telling the truth rather than being fussy -- a per-tenant event slice
            // folded into a single-tenanted document would silently merge tenants in the read model.
            // Every earlier conjoined suite registered no projection, so this pairing first appears
            // with NaturalKeyCompliance's tenanted configuration.
            options.Policies.AllDocumentsAreMultiTenanted();
        }

        config.ApplyTo(new MartenComplianceRegistrar(options));

        _store = new DocumentStore(options);
        _disposables.Add(_store);

        // Marten builds schema lazily, but the compliance suites clean between tests and some
        // of that cleaning is DDL-aware -- get the tables in place up front.
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);
    }

    private static string connectionStringFor(ComplianceStoreConfig config)
    {
        if (!config.MaxPoolSize.HasValue)
        {
            return ConnectionSource.ConnectionString;
        }

        return new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            MaxPoolSize = config.MaxPoolSize.Value
        }.ConnectionString;
    }

    public override IDocumentOperations OpenSession() => _store.LightweightSession();

    // No shared JasperFx interface declares SaveChangesAsync -- in Marten it lives on
    // IDocumentSession, which every session handed out by OpenSession() actually is.
    public override Task SaveChangesAsync(IDocumentOperations session, CancellationToken token)
        => ((IDocumentSession)session).SaveChangesAsync(token);

    public override Task<T?> LoadDocumentAsync<T>(IQuerySession session, object id, CancellationToken token)
        where T : class
        => id switch
        {
            Guid guidId => session.LoadAsync<T>(guidId, token),
            int intId => session.LoadAsync<T>(intId, token),
            long longId => session.LoadAsync<T>(longId, token),
            string stringId => session.LoadAsync<T>(stringId, token),
            _ => throw new ArgumentOutOfRangeException(nameof(id),
                $"Marten cannot load documents by an identity of type {id.GetType().FullName}")
        };

    public override void StoreDocument<T>(IDocumentOperations session, T document) => session.Store(document);

    public override JasperFx.Events.IEventStoreOperations EventsFor(IDocumentOperations session) => session.Events;

    // Session-scoped correlation/causation is shared behavior that no shared interface declares:
    // in Marten the pair hangs off IQuerySession, which every session from OpenSession() is.
    public override string? CorrelationIdFor(IDocumentOperations session) => ((IQuerySession)session).CorrelationId;

    public override string? CausationIdFor(IDocumentOperations session) => ((IQuerySession)session).CausationId;

    public override void SetCorrelationId(IDocumentOperations session, string? correlationId)
        => ((IQuerySession)session).CorrelationId = correlationId;

    // Same seam shape as SetCorrelationId: Marten hangs the user-name metadata off the session
    // as LastModifiedBy, which stamps the user_name column on appended events when
    // MetadataConfig.UserNameEnabled is on (see ComplianceStoreConfig.EnableUserNameTracking).
    public override void SetUserName(IDocumentOperations session, string? userName)
        => ((IDocumentSession)session).LastModifiedBy = userName;

    public override IEventStore EventStore => _store;

    public override IEnumerable<Type> AllAggregateTypes() => _store.Options.Projections.AllAggregateTypes();

    public override IComplianceBatch CreateBatch(IQuerySession session)
        => new MartenComplianceBatch(session.CreateBatchQuery());

    public override IEventRegistry Registry => _store.Options.EventGraph;

    public override async Task CleanEventDataAsync()
    {
        await _store.Advanced.Clean.DeleteAllEventDataAsync().ConfigureAwait(false);
        await _store.Advanced.Clean.DeleteAllDocumentsAsync().ConfigureAwait(false);
    }

    public override async Task<IProjectionDaemon> StartDaemonAsync()
    {
        var daemon = await _store.BuildProjectionDaemonAsync().ConfigureAwait(false);
        _disposables.Add(daemon);

        await daemon.StartAllAsync().ConfigureAwait(false);

        return daemon;
    }

    public override Task WaitForNonStaleProjectionDataAsync(TimeSpan timeout)
        => _store.WaitForNonStaleProjectionDataAsync(timeout);

    // A flat table is not a document, so there is no supported Marten read path for its rows. The
    // schema comes from the store rather than the caller so the compliance suite never has to spell
    // a qualified name, and the reader is deliberately untyped: the suite asserts values, not the
    // Npgsql types they arrive as.
    public override async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryTableAsync(
        string tableName, CancellationToken token)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();

        await using var conn = _store.Storage.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var command = conn.CreateCommand();
        command.CommandText =
            $"select * from {_store.Options.DatabaseSchemaName}.{tableName}";

        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i, token).ConfigureAwait(false)
                    ? null
                    : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    // IEventDataMasking is shared (lifted in jasperfx#635), but the entry point that hands one out
    // is not: Marten spells it on IDocumentStore.Advanced, Polecat on its own, and the two share no
    // interface. This member is the whole of that gap.
    public override Task ApplyEventDataMaskingAsync(
        Action<JasperFx.Events.Protected.IEventDataMasking> configure, CancellationToken token)
        => _store.Advanced.ApplyEventDataMasking(configure, token);

    // ---------------------------------------------------------------------------------------
    // JasperFx 2.64.0 compliance wave (marten#5343). Everything below is either a seam member
    // with a throwing default in the shared fixture, or the capability flag that gates it.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    ///     jasperfx#755. Marten translates the HasTag marker by matching the declaring type in its LINQ
    ///     parser, so the predicate has to be built here where Marten's own extension is in scope; a
    ///     lambda written in the shared source would carry the wrong MethodInfo and never be recognized.
    ///     Deliberately does not validate the tag type -- an unregistered tag must throw at query
    ///     translation, which is the behavior the suite pins.
    /// </summary>
    public override Expression<Func<IEvent, bool>> HasTagFilter<TTag>(TTag value)
        => e => e.HasTag(value);

    /// <summary>
    ///     The single-Where contract is load-bearing rather than incidental: the HasTag facts assert
    ///     that a tag predicate composes with ordinary event predicates inside ONE predicate tree,
    ///     which two chained Where() calls would not exercise.
    /// </summary>
    public override async Task<IReadOnlyList<IEvent>> QueryRawEventsAsync(IQuerySession session,
        Expression<Func<IEvent, bool>> filter, CancellationToken token)
        => await session.Events.QueryAllRawEvents().Where(filter).ToListAsync(token).ConfigureAwait(false);

    public override bool SupportsHasTagLinqPredicates => true;

    /// <summary>
    ///     jasperfx#754. Marten's AggregateToAsync / AggregateToManyAsync ride on QueryAllRawEvents(),
    ///     which returns IMartenQueryable and so is deliberately outside the shared IQueryEventStore
    ///     contract -- hence the seam. Both members apply the optional predicate in a single Where()
    ///     and then hand off to Marten's own terminator; no ordering and no paging, so this cannot
    ///     grow into pinning Marten's operator set.
    /// </summary>
    public override Task<T?> AggregateEventsToAsync<T>(IQuerySession session,
        Expression<Func<IEvent, bool>>? filter, T? initialState, CancellationToken token)
        where T : class
    {
        var queryable = session.Events.QueryAllRawEvents();

        return filter == null
            ? queryable.AggregateToAsync(initialState, token)
            : queryable.Where(filter).AggregateToAsync(initialState, token);
    }

    /// <inheritdoc cref="AggregateEventsToAsync{T}" />
    public override Task<IReadOnlyList<T>> AggregateEventsToManyAsync<T>(IQuerySession session,
        Expression<Func<IEvent, bool>>? filter, CancellationToken token)
        where T : class
    {
        var queryable = session.Events.QueryAllRawEvents();

        return filter == null
            ? queryable.AggregateToManyAsync<T>(token)
            : queryable.Where(filter).AggregateToManyAsync<T>(token);
    }

    public override bool SupportsAggregateToLinqOperators => true;

    /// <summary>
    ///     jasperfx#764 (#4788). Marten maintains an mt_natural_key_X lookup table per aggregate
    ///     carrying a [NaturalKey], written by the auto-registered NaturalKeyProjection, and resolves
    ///     the FetchForWriting / FetchForExclusiveWriting / FetchLatest triple through it.
    /// </summary>
    public override bool SupportsNaturalKeys => true;

    /// <summary>
    ///     jasperfx#762. Both plan types ship on Marten (Marten.FetchStreamStatePlan /
    ///     Marten.FetchStreamPlan), each implementing IQueryPlan&lt;T&gt; and IBatchQueryPlan&lt;T&gt;
    ///     so the same instance can run standalone or inside a batch.
    /// </summary>
    public override bool SupportsStreamQueryPlans => true;

    public override async Task<StreamState?> FetchStreamStateByPlanAsync(
        IQuerySession session, object streamIdentity, bool batched, CancellationToken token)
    {
        var plan = streamIdentity switch
        {
            Guid streamId => new FetchStreamStatePlan(streamId),
            string streamKey => new FetchStreamStatePlan(streamKey),
            _ => throw new ArgumentOutOfRangeException(nameof(streamIdentity),
                $"Marten streams are identified by Guid or string, not {streamIdentity.GetType().FullName}")
        };

        if (!batched)
        {
            return await session.QueryByPlanAsync(plan, token).ConfigureAwait(false);
        }

        // The batched path composes its SQL separately from the standalone one, which is exactly
        // why the suite runs every fact both ways.
        var batch = session.CreateBatchQuery();
        var result = batch.QueryByPlan(plan);
        await batch.Execute(token).ConfigureAwait(false);

        return await result.ConfigureAwait(false);
    }

    /// <inheritdoc cref="FetchStreamStateByPlanAsync" />
    public override async Task<IReadOnlyList<IEvent>> FetchStreamByPlanAsync(
        IQuerySession session, object streamIdentity, long version, bool batched, CancellationToken token)
    {
        var plan = streamIdentity switch
        {
            Guid streamId => new FetchStreamPlan(streamId, version),
            string streamKey => new FetchStreamPlan(streamKey, version),
            _ => throw new ArgumentOutOfRangeException(nameof(streamIdentity),
                $"Marten streams are identified by Guid or string, not {streamIdentity.GetType().FullName}")
        };

        if (!batched)
        {
            return await session.QueryByPlanAsync(plan, token).ConfigureAwait(false);
        }

        var batch = session.CreateBatchQuery();
        var result = batch.QueryByPlan(plan);
        await batch.Execute(token).ConfigureAwait(false);

        return await result.ConfigureAwait(false);
    }

    /// <summary>
    ///     jasperfx#769. A forward, never a re-implementation -- the suite is testing Marten's own
    ///     advertised entry point as much as the shared harness behind it, so inlining the three
    ///     lines Advanced.EventProjectionScenario runs would pass every fact while that entry point
    ///     was missing or wired to the wrong store.
    /// </summary>
    public override Task RunProjectionScenarioAsync(
        Action<JasperFx.Events.TestSupport.ProjectionScenario<IDocumentOperations, IQuerySession>> configure,
        CancellationToken token)
        => _store.Advanced.EventProjectionScenario(scenario => configure(scenario), token);

    /// <summary>
    ///     For the one fact the run entry point structurally cannot reach: a scenario's steps are
    ///     consumed by its first run, so proving a second run fails loudly rather than passing as a
    ///     silent no-op needs a handle on the instance, and the entry point above constructs one and
    ///     throws it away.
    /// </summary>
    public override JasperFx.Events.TestSupport.ProjectionScenario<IDocumentOperations, IQuerySession>
        CreateProjectionScenario()
        => new Marten.Events.TestSupport.ProjectionScenario(_store);

    public override bool SupportsProjectionScenario => true;

    /// <summary>
    ///     PostgreSQL is an MVCC snapshot reader: a second connection reading a row the first
    ///     connection's open transaction is mid-write sees the pre-write snapshot and returns
    ///     immediately rather than blocking on a lock. That is precisely the property this flag is
    ///     asking about, so the before-commit probe cannot deadlock against the hook holding the
    ///     commit open.
    /// </summary>
    public override bool SupportsCommitVisibilityProbe => true;

    public override bool SupportsSubscriptionEventFilters => true;

    /// <summary>
    ///     jasperfx#763. Marten has a real message outbox seam -- Events.MessageOutbox, defaulted to
    ///     NulloMessageOutbox and replaced by the Wolverine integration -- and routes a projection's
    ///     published side effects through it, so the outbox facts run rather than skip.
    /// </summary>
    public override bool SupportsMessageOutbox => true;

    /// <summary>
    ///     jasperfx#732. Marten's documented registration -- AddMarten(...).AddAsyncDaemon(...) --
    ///     is what has to be under test here, not a hand-built daemon, because the gap this suite
    ///     exists to close (fisher#138) was a store that produced no reachable IProjectionCoordinator
    ///     from its documented DI registration while passing all 37 other suites.
    /// </summary>
    public override bool SupportsAncillaryCoordinators => true;

    /// <summary>
    ///     Resolves MARTEN's IProjectionCoordinator&lt;T&gt;, not the shared one. AddMartenStore&lt;T&gt;
    ///     registers the singleton against Marten.Events.Daemon.Coordination.IProjectionCoordinator&lt;T&gt;
    ///     only; that interface derives from the JasperFx one, so the shared seam is satisfied, and
    ///     resolving the product's own marker-typed service is exactly the per-product asymmetry this
    ///     seam exists to absorb.
    /// </summary>
    public override IProjectionCoordinator AncillaryCoordinatorFrom(IServiceProvider services)
        => services
            .GetRequiredService<Marten.Events.Daemon.Coordination.IProjectionCoordinator<IComplianceAncillaryStore>>();

    protected override async Task<IComplianceCoordinatorHost<IDocumentOperations>> StartCoordinatorHostAsync(
        ComplianceStoreConfig config, bool includeAncillaryStore)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        var schemaName = (config.SchemaName ?? "compliance").ToLowerInvariant();

        builder.Services.AddMarten(options =>
            {
                options.Connection(connectionStringFor(config));
                options.AutoCreateSchemaObjects = AutoCreate.All;
                options.DisableNpgsqlLogging = true;
                options.NameDataLength = 100;

                // The same database AND the same schema as the fixture's own store, so the
                // per-test CleanEventDataAsync isolation covers the hosted store too.
                options.DatabaseSchemaName = schemaName;

                if (config.StreamIdentity.HasValue)
                {
                    options.Events.StreamIdentity = config.StreamIdentity.Value;
                }

                config.ApplyTo(new MartenComplianceRegistrar(options));
            })
            .AddAsyncDaemon(DaemonMode.Solo);

        if (includeAncillaryStore)
        {
            // Only ever registered for the one ancillary fact. Load-bearing that it is NOT
            // registered otherwise: the suite asserts the hosted-service walk finds EXACTLY one
            // coordinator, so a second store on the default host would fail every core fact.
            builder.Services.AddMartenStore<IComplianceAncillaryStore>(options =>
                {
                    options.Connection(connectionStringFor(config));
                    options.AutoCreateSchemaObjects = AutoCreate.All;
                    options.DisableNpgsqlLogging = true;
                    options.NameDataLength = 100;
                    options.DatabaseSchemaName = schemaName + "_ancillary";
                })
                .AddAsyncDaemon(DaemonMode.Solo);
        }

        var host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);

        return new MartenCoordinatorHost(host);
    }

    /// <summary>
    ///     The marker type for the ancillary store in the coordinator suite. A fixture-local type
    ///     because every product constrains ancillary markers to its own store interface, so only
    ///     the fixture can name one.
    /// </summary>
    public interface IComplianceAncillaryStore: IDocumentStore;

    private sealed class MartenCoordinatorHost: IComplianceCoordinatorHost<IDocumentOperations>
    {
        private readonly IHost _host;

        public MartenCoordinatorHost(IHost host)
        {
            _host = host;
        }

        public IServiceProvider Services => _host.Services;

        public IDocumentOperations OpenSession()
            => _host.Services.GetRequiredService<IDocumentStore>().LightweightSession();

        public async ValueTask DisposeAsync()
        {
            // IHost.Dispose alone does not call StopAsync, and an abandoned daemon host leaks
            // agents into the next test.
            await _host.StopAsync().ConfigureAwait(false);
            _host.Dispose();
        }
    }

    /// <summary>
    ///     jasperfx#752/#757. Left FALSE deliberately, and this is a real divergence rather than
    ///     unfinished work on this branch. Marten has had event upcasting since v5, but it is
    ///     Marten's OWN implementation: registrations hang off IEventStoreOptions.Upcast and the read
    ///     path routes through EventMapping.IsUpcastTarget, not through the shared
    ///     JasperFx.Events.Upcasting.EventRegistry.Upcasters registry the suite asserts on, and
    ///     Marten implements neither IUpcastPayload nor anything else in that namespace. The shared
    ///     contract was deliberately specified ahead of any store implementing it, so the suite is
    ///     enrolled here skipping wholesale -- which keeps it compiling and running -- and the flag
    ///     flips in the node that moves Marten's read path onto the shared registry.
    /// </summary>
    public override bool SupportsUpcasting => false;

    /// <summary>
    ///     Left FALSE because the operation genuinely does not exist in Marten. ArchiveStream is on
    ///     the shared IEventStoreOptions surface but its reverse is not: Polecat declares
    ///     UnArchiveStream on its own IEventOperations and Marten has no equivalent at all (nothing
    ///     in src/Marten matches "UnArchive"), which is why the shared fixture gates it rather than
    ///     putting it on IEventStoreOperations. The unarchive facts of StreamArchivingCompliance skip;
    ///     every archiving fact around them still runs.
    /// </summary>
    public override bool SupportsUnarchiveStream => false;

    /// <summary>
    ///     Marten names its own six exceptions rather than the ones lifted into JasperFx.Events by
    ///     jasperfx#751.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not an oversight and not a gap. Marten enforces
    ///         CoreTests.all_exceptions_should_derive_from_MartenException:
    ///         every exception Marten throws must derive from MartenException, so that a caller can
    ///         catch the whole family in one clause. C# has single inheritance, so a Marten type
    ///         cannot derive from both MartenException and the lifted JasperFx type -- adopting the
    ///         shared types by subclassing, the way Polecat and Fisher did, would break that
    ///         convention. Marten therefore keeps all six in Marten.Exceptions and nominates them
    ///         here. Revisiting the convention is deferred to Marten 10 (marten#5346).
    ///     </para>
    ///     <para>
    ///         Fully qualified because JasperFx.Events is imported above and declares six types of
    ///         exactly these names; the unqualified name would silently resolve to the wrong one and
    ///         the assertion would fail with a type mismatch rather than a compile error.
    ///     </para>
    /// </remarks>
    public override Type ExceptionTypeFor(ComplianceExceptionKind kind) =>
        kind switch
        {
            ComplianceExceptionKind.UnknownEventType => typeof(Marten.Exceptions.UnknownEventTypeException),
            ComplianceExceptionKind.NonExistentStream => typeof(Marten.Exceptions.NonExistentStreamException),
            ComplianceExceptionKind.ExistingStreamIdCollision =>
                typeof(Marten.Exceptions.ExistingStreamIdCollisionException),
            ComplianceExceptionKind.EventDeserializationFailure =>
                typeof(Marten.Exceptions.EventDeserializationFailureException),
            ComplianceExceptionKind.StreamLocked => typeof(Marten.Exceptions.StreamLockedException),
            ComplianceExceptionKind.DefaultTenantUsageDisabled =>
                typeof(Marten.Exceptions.DefaultTenantUsageDisabledException),
            _ => base.ExceptionTypeFor(kind)
        };

    public override async ValueTask DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            switch (disposable)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable syncDisposable:
                    syncDisposable.Dispose();
                    break;
            }
        }

        _disposables.Clear();
    }

    internal class MartenComplianceRegistrar: IComplianceStoreRegistrar
    {
        private readonly StoreOptions _options;

        public MartenComplianceRegistrar(StoreOptions options)
        {
            _options = options;
        }

        public void AddEventType(Type eventType) => _options.Events.AddEventType(eventType);

        /// <summary>
        ///     jasperfx#669: both binary-serialization members take the promoted
        ///     <see cref="JasperFx.Events.IEventBinarySerializer" />, which Marten's registration surface was
        ///     widened to accept in 9.26. Before that widening these could only have been implemented by
        ///     wrapping the compliance suite's serializer in a Marten-namespaced adapter — the exact
        ///     per-store duplication the promotion exists to delete.
        /// </summary>
        public void UseBinarySerializer<TEvent>(JasperFx.Events.IEventBinarySerializer serializer)
            where TEvent : notnull
            => _options.Events.UseBinarySerializer<TEvent>(serializer);

        /// <inheritdoc cref="UseBinarySerializer{TEvent}" />
        public void SetDefaultBinarySerializer(JasperFx.Events.IEventBinarySerializer serializer)
            => _options.Events.DefaultBinarySerializer = serializer;

        public ITagTypeRegistration RegisterTagType<TTag>(string tableSuffix) where TTag : notnull
            => _options.Events.RegisterTagType<TTag>(tableSuffix);

        public void Snapshot<TDoc>(SnapshotLifecycle lifecycle) where TDoc : notnull
            => _options.Projections.Snapshot<TDoc>(lifecycle);

        /// <summary>
        ///     jasperfx#674 (#5251). Two lines against the shared <c>EventRegistry</c> surface, which is
        ///     the point: the enrollment and the cache slot are both inherited, and all the registrar
        ///     supplies is which options object the store hangs its event registry off.
        /// </summary>
        public void CacheAggregatesForWriting<TDoc>(JasperFx.Events.Fetching.IAggregateWriteCache cache)
            where TDoc : class
        {
            _options.Events.AggregateWriteCaching.Cache = cache;
            _options.Events.CacheAggregatesForWriting<TDoc>();
        }

        public void LiveAggregation<TDoc>() where TDoc : notnull
            => _options.Projections.LiveStreamAggregation<TDoc>();

        /// <summary>
        ///     Marten needs a strong-typed identifier registered before it can use it in LINQ and identity
        ///     mapping, so this maps straight onto StoreOptions.RegisterValueType. Polecat implements the same
        ///     seam as a no-op because it derives the same information from ValueTypeInfo when it builds the
        ///     document mapping — the asymmetry the seam exists to absorb.
        /// </summary>
        public void RegisterValueType<TValue>() where TValue : notnull
            => _options.RegisterValueType<TValue>();

        public void AddMaskingRule<TEvent>(Action<TEvent> rule) where TEvent : notnull
            => _options.Events.AddMaskingRuleForProtectedInformation(rule);

        public void AddMaskingRule<TEvent>(Func<TEvent, TEvent> rule) where TEvent : notnull
            => _options.Events.AddMaskingRuleForProtectedInformation(rule);

        /// <summary>
        ///     The name is pinned because progression is keyed on it and the products disagree on what
        ///     an unnamed subscription defaults to.
        ///
        ///     jasperfx#768 (#5343) adds the filter replay. A list declared on the shared subscription
        ///     object reaches nothing on its own: Marten wraps a bare ISubscription in its own
        ///     SubscriptionWrapper, and it is the WRAPPER the daemon reads filters from. Replaying each
        ///     declared type onto the wrapper's IncludeType is the whole of what
        ///     SupportsSubscriptionEventFilters gates.
        /// </summary>
        public void Subscribe(ComplianceSubscription subscription)
            => _options.Projections.Subscribe(subscription, x =>
            {
                x.Name = ComplianceSubscription.SubscriptionName;

                foreach (var eventType in subscription.IncludedEventTypes)
                {
                    x.IncludeType(eventType);
                }
            });

        /// <summary>
        ///     jasperfx#763 (#5343). Every store spells this the same way; the seam exists because
        ///     IMessageOutbox is a per-product type, so the shared config cannot declare the property.
        /// </summary>
        public void UseMessageOutbox(RecordingMessageOutbox outbox)
            => _options.Events.MessageOutbox = outbox;

        public void AddProjection(ProjectionBase projection, ProjectionLifecycle lifecycle)
            => _options.Projections.Add((IProjectionSource<IDocumentOperations, IQuerySession>)projection, lifecycle);

        /// <summary>
        ///     jasperfx#725 (#5335) — exactly the forward-plus-adapter the seam documents: the calls are
        ///     identical across the products but the composite type is Marten's own, so the adapter's only
        ///     job is to drop the DocumentMappingExpression return value that Marten's Snapshot has and the
        ///     void-returning shared member deliberately does not.
        /// </summary>
        public void AddCompositeProjection(string name, Action<IComplianceCompositeBuilder> configure)
            => _options.Projections.CompositeProjectionFor(name,
                composite => configure(new MartenCompositeBuilder(composite)));

        private sealed class MartenCompositeBuilder: IComplianceCompositeBuilder
        {
            private readonly Marten.Events.Projections.CompositeProjection _composite;

            public MartenCompositeBuilder(Marten.Events.Projections.CompositeProjection composite)
            {
                _composite = composite;
            }

            public void Snapshot<TDoc>(int stageNumber) where TDoc : notnull
                => _composite.Snapshot<TDoc>(stageNumber);
        }
    }

    internal class MartenComplianceBatch: IComplianceBatch
    {
        private readonly IBatchedQuery _batch;

        public MartenComplianceBatch(IBatchedQuery batch)
        {
            _batch = batch;
        }

        public Task<bool> EventsExist(EventTagQuery query) => _batch.Events.EventsExist(query);

        public Task<IEventBoundary<T>> FetchForWritingByTags<T>(EventTagQuery query) where T : class
            => _batch.Events.FetchForWritingByTags<T>(query);

        public Task Execute(CancellationToken token = default) => _batch.Execute(token);
    }
}
