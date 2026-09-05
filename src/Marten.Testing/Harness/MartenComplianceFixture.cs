using System;
using System.Collections.Generic;
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

        public void Subscribe(ComplianceSubscription subscription)
            => _options.Projections.Subscribe(subscription,
                x => x.Name = ComplianceSubscription.SubscriptionName);

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
