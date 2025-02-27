using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using JasperFx.Core;
using Marten.Events.Aggregation;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Patching;
using Marten.Services;

namespace Marten.Events.Daemon.Internals;
#nullable enable

/// <summary>
///     Incrementally built batch command for projection updates
/// </summary>
public class ProjectionUpdateBatch: IUpdateBatch, IAsyncDisposable, IDisposable, ISessionWorkTracker
{
    private readonly List<Type> _documentTypes = new();
    private readonly List<OperationPage> _pages = new();
    private readonly DaemonSettings _settings;
    private readonly CancellationToken _token;
    private OperationPage? _current;
    private DocumentSessionBase? _session;

    private IMartenSession Session
    {
        get => _session ?? throw new InvalidOperationException("Session already released");
    }

    public List<IChangeListener> Listeners { get; } = new();

    public ShardExecutionMode Mode { get; }

    public bool ShouldApplyListeners { get; set; }

    internal ProjectionUpdateBatch(DaemonSettings settings,
        DocumentSessionBase? session, ShardExecutionMode mode, CancellationToken token)
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

        startNewPage(session);
    }

    public async Task WaitForCompletion()
    {
        Queue.Complete();

        await Queue.Completion.ConfigureAwait(false);
        foreach (var patch in _patches)
        {
            applyOperation(patch);
        }
    }

    // TODO -- make this private
    public ActionBlock<IStorageOperation> Queue { get; }



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

    void ISessionWorkTracker.Sort(StoreOptions options)
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
        if (!listeners.Any()) return;

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
        if (!listeners.Any()) return;

        var unitOfWorkData = new UnitOfWork(_pages.SelectMany(x => x.Operations));
        foreach (var listener in listeners)
        {
            await listener.BeforeCommitAsync((IDocumentSession)session, unitOfWorkData, _token)
                .ConfigureAwait(false);
        }
    }

    private void startNewPage(IMartenSession session)
    {
        if (_token.IsCancellationRequested)
            return;

        _current = new OperationPage(session);
        _pages.Add(_current);
    }

    private readonly List<IStorageOperation> _patches = new();

    private void processOperation(IStorageOperation operation)
    {
        if (_token.IsCancellationRequested)
            return;

        if (operation is PatchOperation)
        {
            _patches.Add(operation);
            return;
        }

        applyOperation(operation);
    }

    private void applyOperation(IStorageOperation operation)
    {
        _current.Append(operation);

        _documentTypes.Fill(operation.DocumentType);

        if (_session != null && !_token.IsCancellationRequested && _current.Count >= Session.Options.UpdateBatchSize)
        {
            startNewPage(Session);
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


    public ValueTask CloseSession() => DisposeAsync();

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        Queue.Complete();
        foreach (var page in _pages) page.ReleaseSession();

        if (_session != null)
        {
            await _session.DisposeAsync().ConfigureAwait(true);
            _session = null;
        }

        Dispose(disposing: false);
        GC.SuppressFinalize(this);
    }

    protected void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        Queue.Complete();

        foreach (var page in _pages) page.ReleaseSession();

        _session?.Dispose();

        _session = null;
    }

    private IMessageBatch? _batch;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    public async ValueTask<IMessageBatch> CurrentMessageBatch(DocumentSessionBase session)
    {
        if (_batch != null) return _batch;

        await _semaphore.WaitAsync(_token).ConfigureAwait(false);

        if (_batch != null) return _batch;

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
}
