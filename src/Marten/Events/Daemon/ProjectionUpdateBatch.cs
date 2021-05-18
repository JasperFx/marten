using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Weasel.Postgresql;
using Marten.Services;
using Npgsql;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Incrementally built batch command for projection updates
    /// </summary>
    public class ProjectionUpdateBatch : IUpdateBatch, IDisposable, ISessionWorkTracker
    {
        public EventRange Range { get; }
        private readonly DocumentSessionBase _session;
        private readonly CancellationToken _token;
        private readonly IList<Page> _pages = new List<Page>();
        private Page _current;

        internal ProjectionUpdateBatch(EventGraph events, DocumentSessionBase session, EventRange range, CancellationToken token)
        {
            Range = range;
            _session = session;
            _token = token;
            Queue = new ActionBlock<IStorageOperation>(processOperation,
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = 1, EnsureOrdered = true, CancellationToken = token});

            startNewPage(session);

            var progressOperation = range.BuildProgressionOperation(events);
            Queue.Post(progressOperation);
        }

        public ActionBlock<IStorageOperation> Queue { get; }

        private void startNewPage(IMartenSession session)
        {
            _current = new Page(session);
            _pages.Add(_current);
        }

        private void processOperation(IStorageOperation operation)
        {
            if (_token.IsCancellationRequested) return;

            _current.Append(operation);

            if (_current.Count >= _session.Options.UpdateBatchSize)
            {
                startNewPage(_session);
            }
        }

        void IUpdateBatch.ApplyChanges(IMartenSession session)
        {
            if (_token.IsCancellationRequested) return;

            var exceptions = new List<Exception>();
            foreach (var page in _pages)
            {
                page.ApplyChanges(exceptions);

                // Wanna fail fast here instead of trying the next batch
                if (exceptions.Any())
                {
                    throw new AggregateException(exceptions);
                }
            }
        }

        async Task IUpdateBatch.ApplyChangesAsync(IMartenSession session, CancellationToken token)
        {
            if (_token.IsCancellationRequested) return;

            var exceptions = new List<Exception>();
            foreach (var page in _pages)
            {
                await page.ApplyChangesAsync(exceptions, token);

                // Wanna fail fast here instead of trying the next batch
                if (exceptions.Any())
                {
                    throw new AggregateException(exceptions);
                }
            }
        }

        public class Page
        {
            private readonly IMartenSession _session;
            public int Count { get; private set; }

            private readonly NpgsqlCommand _command = new NpgsqlCommand();
            private readonly CommandBuilder _builder;
            private readonly List<IStorageOperation> _operations = new List<IStorageOperation>();


            public Page(IMartenSession session)
            {
                _session = session;
                _builder = new CommandBuilder(_command);
            }

            public void Append(IStorageOperation operation)
            {
                Count++;
                operation.ConfigureCommand(_builder, _session);
                _builder.Append(";");
                _operations.Add(operation);
            }

            public void ApplyChanges(IList<Exception> exceptions)
            {
                _command.CommandText = _builder.ToString();

                using var reader = _session.Database.ExecuteReader(_command);
                UpdateBatch.ApplyCallbacks(_operations, reader, exceptions);
            }

            public async Task ApplyChangesAsync(IList<Exception> exceptions, CancellationToken token)
            {
                _command.CommandText = _builder.ToString();

                using var reader = await _session.Database.ExecuteReaderAsync(_command, token);
                await UpdateBatch.ApplyCallbacksAsync(_operations, reader, exceptions, token);
            }
        }

        public void Dispose()
        {
            _session.Dispose();
            Queue.Complete();
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
    }
}
