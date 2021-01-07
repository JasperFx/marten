using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Util;
using Npgsql;

namespace Marten.Internal
{
    internal class IncrementalUpdateBatch : IUpdateBatch, IDisposable
    {
        private readonly DocumentSessionBase _session;
        private readonly ActionBlock<IStorageOperation> _block;
        private readonly IList<Page> _pages = new List<Page>();
        private Page _current;

        public IncrementalUpdateBatch(DocumentSessionBase session)
        {
            _session = session;
            _block = new ActionBlock<IStorageOperation>(processOperation,
                new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = 1, EnsureOrdered = true});

            startNewPage(session);
        }

        private void startNewPage(IMartenSession session)
        {
            _current = new Page(session);
            _pages.Add(_current);
        }

        private void processOperation(IStorageOperation operation)
        {
            _current.Append(operation);

            if (_current.Count >= _session.Options.UpdateBatchSize)
            {
                startNewPage(_session);
            }
        }



        public void Enqueue(IStorageOperation operation)
        {
            _block.Post(operation);
        }

        public Task Completion => _block.Completion;

        public void ApplyChanges(IMartenSession session)
        {
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

        public async Task ApplyChangesAsync(IMartenSession session, CancellationToken token)
        {
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
                using var reader = _session.Database.ExecuteReader(_command);
                UpdateBatch.ApplyCallbacks(_operations, reader, exceptions);
            }

            public async Task ApplyChangesAsync(IList<Exception> exceptions, CancellationToken token)
            {
                using var reader = await _session.Database.ExecuteReaderAsync(_command, token);
                await UpdateBatch.ApplyCallbacksAsync(_operations, reader, exceptions, token);
            }
        }

        public void Dispose()
        {
            _block.Complete();
        }
    }
}
