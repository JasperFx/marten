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

        if (config.EnableHeaders)
        {
            options.Events.MetadataConfig.HeadersEnabled = true;
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

        public ITagTypeRegistration RegisterTagType<TTag>(string tableSuffix) where TTag : notnull
            => _options.Events.RegisterTagType<TTag>(tableSuffix);

        public void Snapshot<TDoc>(SnapshotLifecycle lifecycle) where TDoc : notnull
            => _options.Projections.Snapshot<TDoc>(lifecycle);

        public void LiveAggregation<TDoc>() where TDoc : notnull
            => _options.Projections.LiveStreamAggregation<TDoc>();

        public void AddProjection(ProjectionBase projection, ProjectionLifecycle lifecycle)
            => _options.Projections.Add((IProjectionSource<IDocumentOperations, IQuerySession>)projection, lifecycle);
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
