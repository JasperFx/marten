using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.Internals;
#nullable enable

/// <summary>
///     Incrementally built batch command for projection updates
/// </summary>
public class ProjectionUpdateBatch: IUpdateBatch, IAggregation, IDisposable, ISessionWorkTracker
{
    private readonly List<Type> _documentTypes = new();
    private readonly List<OperationPage> _pages = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly DocumentSessionBase _session;
    private readonly ProjectionOptions _settings;
    private readonly CancellationToken _token;

    private IMessageBatch? _batch;
    private OperationPage? _current;

    internal ProjectionUpdateBatch(ProjectionOptions settings, DocumentSessionBase session, ShardExecutionMode mode,
        CancellationToken token)
    {
        _settings = settings;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _token = token;
        Mode = mode;
        Queue = new ActionBlock<IStorageOperation>(processOperation,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1, EnsureOrdered = true, CancellationToken = token
            });

        startNewPage(_session);
    }

    internal ProjectionUpdateBatch(DocumentStore store, IMartenDatabase database, ShardExecutionMode mode,
        CancellationToken token)
    {
        _settings = store.Options.Projections;
        _session = (DocumentSessionBase)store.OpenSession(SessionOptions.ForDatabase(database));
        _token = token;
        Mode = mode;
        Queue = new ActionBlock<IStorageOperation>(processOperation,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1, EnsureOrdered = true, CancellationToken = token
            });

        startNewPage(_session);
    }

    private IMartenSession Session => _session ?? throw new InvalidOperationException("Session already released");

    public List<IChangeListener> Listeners { get; } = new();

    public ShardExecutionMode Mode { get; }

    public bool ShouldApplyListeners { get; set; }

    // TODO -- make this private
    public ActionBlock<IStorageOperation> Queue { get; }

    public Task ExecuteAsync(CancellationToken token)
    {
        return _session.ExecuteBatchAsync(this, token);
    }

    public async ValueTask DisposeAsync()
    {
        Queue.Complete();
        foreach (var page in _pages) page.ReleaseSession();

        if (_session != null)
        {
            await _session.DisposeAsync().ConfigureAwait(true);
        }

        Dispose(false);
        GC.SuppressFinalize(this);
    }


    public async Task ProcessAggregationAsync<TDoc, TId>(EventSliceGroup<TDoc, TId> grouping, CancellationToken token)
    {
        // TODO -- put this logic of finding the runtime somewhere a bit more encapsulated
        if (!_session.Options.Projections.TryFindAggregate(typeof(TDoc), out var projection))
        {
            throw new ArgumentOutOfRangeException(
                $"No known aggregation runtime for document type {typeof(TDoc).FullNameInCode()}");
        }

        var store = (DocumentStore)_session.DocumentStore;
        var runtime = (IAggregationRuntime<TDoc, TId>)projection.BuildRuntime(store);
        // TODO -- encapsulate ending

        using var session = new ProjectionDocumentSession(store, this,
            new SessionOptions
            {
                Tracking = DocumentTracking.None, Tenant = new Tenant(grouping.TenantId, _session.Database)
            }, Mode);

        var builder = new ActionBlock<EventSlice<TDoc, TId>>(async slice =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            // TODO -- emit exceptions in one place
            await runtime.ApplyChangesAsync(session, slice, token, ProjectionLifecycle.Async)
                .ConfigureAwait(false);
        }, new ExecutionDataflowBlockOptions { CancellationToken = token });

        await processEventSlices(builder, runtime, store, grouping, token).ConfigureAwait(false);

        if (builder != null)
        {
            builder.Complete();
            await builder.Completion.ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    IEnumerable<IDeletion> IUnitOfWork.Deletions()
    {
        throw new NotSupportedException();
    }

    IEnumerable<IDeletion> IUnitOfWork.DeletionsFor<T>()
    {
        throw new NotSupportedException();
    }

    IEnumerable<IDeletion> IUnitOfWork.DeletionsFor(Type documentType)
    {
        throw new NotSupportedException();
    }

    IEnumerable<object> IUnitOfWork.Updates()
    {
        throw new NotSupportedException();
    }

    IEnumerable<object> IUnitOfWork.Inserts()
    {
        throw new NotSupportedException();
    }

    IEnumerable<T> IUnitOfWork.UpdatesFor<T>()
    {
        throw new NotSupportedException();
    }

    IEnumerable<T> IUnitOfWork.InsertsFor<T>()
    {
        throw new NotSupportedException();
    }

    IEnumerable<T> IUnitOfWork.AllChangedFor<T>()
    {
        throw new NotSupportedException();
    }

    IList<StreamAction> IUnitOfWork.Streams()
    {
        throw new NotSupportedException();
    }

    IEnumerable<IStorageOperation> IUnitOfWork.Operations()
    {
        throw new NotSupportedException();
    }

    IEnumerable<IStorageOperation> IUnitOfWork.OperationsFor<T>()
    {
        throw new NotSupportedException();
    }

    IEnumerable<IStorageOperation> IUnitOfWork.OperationsFor(Type documentType)
    {
        throw new NotSupportedException();
    }

    IEnumerable<object> IChangeSet.Updated => throw new NotSupportedException();

    IEnumerable<object> IChangeSet.Inserted => throw new NotSupportedException();

    IEnumerable<IDeletion> IChangeSet.Deleted => throw new NotSupportedException();

    IEnumerable<IEvent> IChangeSet.GetEvents()
    {
        throw new NotSupportedException();
    }

    IEnumerable<StreamAction> IChangeSet.GetStreams()
    {
        throw new NotSupportedException();
    }

    IChangeSet IChangeSet.Clone()
    {
        throw new NotSupportedException();
    }

    void ISessionWorkTracker.Reset()
    {
        throw new NotSupportedException();
    }

    void ISessionWorkTracker.Add(IStorageOperation operation)
    {
        Queue.Post(operation);
    }

    void ISessionWorkTracker.Sort()
    {
        throw new NotSupportedException();
    }

    List<StreamAction> ISessionWorkTracker.Streams => new();


    IReadOnlyList<IStorageOperation> ISessionWorkTracker.AllOperations => throw new NotSupportedException();

    void ISessionWorkTracker.Eject<T>(T document)
    {
        throw new NotSupportedException();
    }

    void ISessionWorkTracker.EjectAllOfType(Type type)
    {
        throw new NotSupportedException();
    }

    bool ISessionWorkTracker.TryFindStream(string streamKey, out StreamAction stream)
    {
        throw new NotSupportedException();
    }

    bool ISessionWorkTracker.TryFindStream(Guid streamId, out StreamAction stream)
    {
        throw new NotSupportedException();
    }

    bool ISessionWorkTracker.HasOutstandingWork()
    {
        throw new NotSupportedException();
    }

    public void EjectAll()
    {
        throw new NotSupportedException();
    }

    public void PurgeOperations<T, TId>(TId id) where T : notnull
    {
        // Do nothing here
    }

    public IReadOnlyList<Type> DocumentTypes()
    {
        return _documentTypes;
    }

    public async Task PostUpdateAsync(IMartenSession session)
    {
        if (!ShouldApplyListeners)
        {
            return;
        }

        var listeners = _settings.AsyncListeners.Concat(Listeners).ToArray();
        if (!listeners.Any())
        {
            return;
        }

        var unitOfWorkData = new UnitOfWork(_pages.SelectMany(x => x.Operations));
        foreach (var listener in listeners)
        {
            await listener.AfterCommitAsync((IDocumentSession)session, unitOfWorkData, _token)
                .ConfigureAwait(false);
        }
    }

    public async Task PreUpdateAsync(IMartenSession session)
    {
        if (!ShouldApplyListeners)
        {
            return;
        }

        var listeners = _settings.AsyncListeners.Concat(Listeners).ToArray();
        if (!listeners.Any())
        {
            return;
        }

        var unitOfWorkData = new UnitOfWork(_pages.SelectMany(x => x.Operations));
        foreach (var listener in listeners)
        {
            await listener.BeforeCommitAsync((IDocumentSession)session, unitOfWorkData, _token)
                .ConfigureAwait(false);
        }
    }

    public IReadOnlyList<OperationPage> BuildPages(IMartenSession session)
    {
        if (_token.IsCancellationRequested)
        {
            return Array.Empty<OperationPage>();
        }

        // Guard against empty batches
        return _pages.Where(x => x.Operations.Any()).ToList();
    }

    public Task WaitForCompletion()
    {
        Queue.Complete();
        return Queue.Completion;
    }

    private void startNewPage(IMartenSession session)
    {
        if (_token.IsCancellationRequested)
        {
            return;
        }

        _current = new OperationPage(session, new BatchBuilder());
        _pages.Add(_current);
    }

    private void processOperation(IStorageOperation operation)
    {
        if (_token.IsCancellationRequested)
        {
            return;
        }

        _current.Append(operation);

        _documentTypes.Fill(operation.DocumentType);

        if (_session != null && !_token.IsCancellationRequested && _current.Count >= Session.Options.UpdateBatchSize)
        {
            startNewPage(Session);
        }
    }


    public ValueTask CloseSession()
    {
        return DisposeAsync();
    }

    protected void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        Queue.Complete();

        foreach (var page in _pages) page.ReleaseSession();

        _session?.Dispose();
    }

    public async ValueTask<IMessageBatch> CurrentMessageBatch(DocumentSessionBase session)
    {
        if (_batch != null)
        {
            return _batch;
        }

        await _semaphore.WaitAsync(_token).ConfigureAwait(false);

        if (_batch != null)
        {
            return _batch;
        }

        try
        {
            _batch = await _session.Options.Events.MessageOutbox.CreateBatch(session).ConfigureAwait(false);
            Listeners.Add(_batch);

            return _batch;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task processEventSlices<TDoc, TId>(ActionBlock<EventSlice<TDoc, TId>> builder,
        IAggregationRuntime<TDoc, TId> runtime,
        IDocumentStore store, EventSliceGroup<TDoc, TId> grouping, CancellationToken token)
    {
        var tenant = new Tenant(grouping.TenantId, _session.Database);

        var cache = runtime.CacheFor(tenant);
        var beingFetched = new List<EventSlice<TDoc, TId>>();
        foreach (var slice in grouping.Slices)
        {
            if (token.IsCancellationRequested)
            {
                builder.Complete();
                break;
            }

            if (runtime.IsNew(slice))
            {
                builder.Post(slice);

                // Don't use it any farther, it's ready to do its thing
                grouping.Slices.Remove(slice.Id);
            }
            else if (cache.TryFind(slice.Id, out var aggregate))
            {
                slice.Aggregate = aggregate;
                builder.Post(slice);

                // Don't use it any farther, it's ready to do its thing
                grouping.Slices.Remove(slice.Id);
            }
            else
            {
                beingFetched.Add(slice);
            }
        }

        if (token.IsCancellationRequested || !beingFetched.Any())
        {
            cache.CompactIfNecessary();
            return;
        }

        // Minor optimization
        if (!beingFetched.Any())
        {
            return;
        }

        var ids = beingFetched.Select(x => x.Id).ToArray();

        var options = new SessionOptions { Tenant = tenant, AllowAnyTenant = true };

        await using var session = (IMartenSession)store.LightweightSession(options);
        var aggregates = await runtime.Storage
            .LoadManyAsync(ids, session, token).ConfigureAwait(false);

        if (token.IsCancellationRequested || aggregates == null)
        {
            return;
        }

        var dict = aggregates.ToDictionary(x => runtime.Storage.Identity(x));

        foreach (var slice in grouping.Slices)
        {
            if (dict.TryGetValue(slice.Id, out var aggregate))
            {
                slice.Aggregate = aggregate;
                cache.Store(slice.Id, aggregate);
            }

            builder?.Post(slice);
        }
    }
}
