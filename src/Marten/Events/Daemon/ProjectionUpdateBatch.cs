using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using JasperFx.Core;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Services;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Events.Daemon;
#nullable enable

/// <summary>
///     Incrementally built batch command for projection updates
/// </summary>
public class ProjectionUpdateBatch: IUpdateBatch, IAsyncDisposable, IDisposable, ISessionWorkTracker
{
    private readonly List<Type> _documentTypes = new();
    private readonly ShardExecutionMode _mode;
    private readonly IList<Page> _pages = new List<Page>();
    private readonly DaemonSettings _settings;
    private readonly CancellationToken _token;
    private Page _current;
    private DocumentSessionBase? _session;

    private IMartenSession Session
    {
        get => _session ?? throw new InvalidOperationException("Session already released");
    }

    internal ProjectionUpdateBatch(EventGraph events, DaemonSettings settings,
        DocumentSessionBase? session, EventRange range, CancellationToken token, ShardExecutionMode mode)
    {
        Range = range;
        _settings = settings;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _token = token;
        _mode = mode;
        Queue = new ActionBlock<IStorageOperation>(processOperation,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = 1, EnsureOrdered = true, CancellationToken = token
            });

        startNewPage(session);

        var progressOperation = range.BuildProgressionOperation(events);
        Queue.Post(progressOperation);
    }

    public EventRange Range { get; }

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

    List<StreamAction> ISessionWorkTracker.Streams => throw new NotSupportedException();


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

    void IUpdateBatch.ApplyChanges(IMartenSession session)
    {
        if (_token.IsCancellationRequested)
        {
            return;
        }

        var exceptions = new List<Exception>();
        foreach (var page in _pages)
        {
            page.ApplyChanges(exceptions, session);

            // Wanna fail fast here instead of trying the next batch
            if (exceptions.Any())
            {
                throw new AggregateException(exceptions);
            }
        }
    }

    async Task IUpdateBatch.ApplyChangesAsync(IMartenSession session, CancellationToken token)
    {
        if (_token.IsCancellationRequested)
        {
            return;
        }

        if (session.Options.AutoCreateSchemaObjects != AutoCreate.None)
        {
            foreach (var documentType in _documentTypes)
                await session.Database.EnsureStorageExistsAsync(documentType, token).ConfigureAwait(false);
        }

        var exceptions = new List<Exception>();
        foreach (var page in _pages)
        {
            await page.ApplyChangesAsync(exceptions, session, token).ConfigureAwait(false);

            // Wanna fail fast here instead of trying the next batch
            if (exceptions.Any())
            {
                throw new AggregateException(exceptions);
            }
        }

        if (_mode == ShardExecutionMode.Continuous && _settings.AsyncListeners.Any())
        {
            var unitOfWorkData = new UnitOfWork(_pages.SelectMany(x => x.Operations));
            foreach (var listener in _settings.AsyncListeners)
            {
                await listener.AfterCommitAsync((IDocumentSession)session, unitOfWorkData, _token)
                    .ConfigureAwait(false);
            }
        }
    }

    private void startNewPage(IMartenSession session)
    {
        if (_token.IsCancellationRequested)
            return;

        _current = new Page(session);
        _pages.Add(_current);
    }

    private void processOperation(IStorageOperation operation)
    {
        if (_token.IsCancellationRequested)
            return;

        _current.Append(operation);

        _documentTypes.Fill(operation.DocumentType);

        if (!_token.IsCancellationRequested && _current.Count >= Session.Options.UpdateBatchSize)
        {
            startNewPage(Session);
        }
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

    public class Page
    {
        private readonly CommandBuilder _builder;

        private readonly NpgsqlCommand _command = new();
        private readonly List<IStorageOperation> _operations = new();
        private IMartenSession? _session;


        public Page(IMartenSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _builder = new CommandBuilder(_command);
        }

        public int Count { get; private set; }
        public IEnumerable<IStorageOperation> Operations => _operations;

        public void Append(IStorageOperation operation)
        {
            Count++;
            operation.ConfigureCommand(
                _builder,
                _session ?? throw new InvalidOperationException("Session already released!")
            );
            _builder.Append(";");
            _operations.Add(operation);
        }

        public void ApplyChanges(IList<Exception> exceptions, IMartenSession session)
        {
            if (Count == 0)
            {
                return;
            }

            _command.CommandText = _builder.ToString();

            using var reader = session.ExecuteReader(_command);
            UpdateBatch.ApplyCallbacks(_operations, reader, exceptions);
        }

        public async Task ApplyChangesAsync(IList<Exception> exceptions, IMartenSession session,
            CancellationToken token)
        {
            if (Count == 0)
            {
                return;
            }

            _command.CommandText = _builder.ToString();

            await using var reader = await session.ExecuteReaderAsync(_command, token).ConfigureAwait(false);
            await UpdateBatch.ApplyCallbacksAsync(_operations, reader, exceptions, token).ConfigureAwait(false);
        }

        public void ReleaseSession()
        {
            _session = null;
        }
    }
}
