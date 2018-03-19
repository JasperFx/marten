using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Storage;
using Npgsql;

namespace Marten.Services
{
    public class UpdateBatch : IDisposable
    {
        private readonly CharArrayTextWriter.IPool _writerPool;
        private readonly Stack<BatchCommand> _commands = new Stack<BatchCommand>(); 
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private readonly List<CharArrayTextWriter> _writers = new List<CharArrayTextWriter>();
        private readonly DocumentStore _store;
        private readonly ITenant _tenant;

        public UpdateBatch(DocumentStore store, IManagedConnection connection, VersionTracker versions, CharArrayTextWriter.IPool writerPool, ITenant tenant, ConcurrencyChecks concurrency)
        {
            _store = store;
            _writerPool = writerPool;
            Versions = versions ?? throw new ArgumentNullException(nameof(versions));

            _commands.Push(new BatchCommand(_store.Serializer, tenant));
            Connection = connection;
            Concurrency = concurrency;
            TenantId = tenant.TenantId;
            _tenant = tenant;
        }

        public ISerializer Serializer => _store.Serializer;

        public VersionTracker Versions { get; }

        public IManagedConnection Connection { get; }
        public ConcurrencyChecks Concurrency { get; }

        public CharArrayTextWriter GetWriter()
        {
            var writer = _writerPool.Lease();
            _writers.Add(writer);
            return writer;
        }

        public void Dispose()
        {
            Connection.Dispose();
        }

        public BatchCommand Current()
        {
            return _lock.MaybeWrite(
                () => _commands.Peek(),
                () => _commands.Peek().Count >= _store.Options.UpdateBatchSize,
                () => _commands.Push(new BatchCommand(Serializer, _tenant))
            );
        }


        public void Add(IStorageOperation operation)
        {
            var batch = Current();

            batch.AddCall(operation, operation as ICallback);
        }

        public SprocCall Sproc(DbObjectName function, ICallback callback = null, IExceptionTransform exceptionTransform = null)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));

            return Current().Sproc(function, callback, exceptionTransform);
        }

        public void Execute()
        {
            var list = new List<Exception>();

            try
            {
                // Gotta do the reverse to run these in the right order
                foreach (var batch in _commands.Reverse().ToArray())
                {                    
                    var cmd = batch.BuildCommand();
                    try
                    {
                        Connection.Execute(cmd, c =>
                        {
                            if (batch.HasCallbacks())
                            {
                                executeCallbacks(cmd, batch, list);
                            }
                            else
                            {
                                cmd.ExecuteNonQuery();
                            }
                        });
                    }
                    catch (Exception e) when (batch.HasExceptionTransforms())
                    {
                        Exception transformed = null;
                        if (batch.ExceptionTransforms.Any(x => x != null && x.TryTransform(e, out transformed)))
                        {
                            throw transformed;
                        }
                        throw;
                    }
                }

                if (list.Any())
                    throw new AggregateException(list);
            }
            finally
            {
                if (_writerPool != null)
                {
                    _writerPool?.Release(_writers);
                    _writers.Clear();
                }
            }
        }

        private static void executeCallbacks(NpgsqlCommand cmd, BatchCommand batch, List<Exception> list)
        {
            using (var reader = cmd.ExecuteReader())
            {
                if (batch.Callbacks.Any())
                {
                    batch.Callbacks[0]?.Postprocess(reader, list);

                    for (var i = 1; i < batch.Callbacks.Count; i++)
                    {
                        if (!(batch.Calls[i - 1] is NoDataReturnedCall))
                        {
                            reader.NextResult();
                        }

                        batch.Callbacks[i]?.Postprocess(reader, list);
                    }
                }
            }
        }

        public async Task ExecuteAsync(CancellationToken token)
        {
            try
            {
                var list = new List<Exception>();
                foreach (var batch in _commands.ToArray())
                {
                    var cmd = batch.BuildCommand();
                    await Connection.ExecuteAsync(cmd, async (c, tkn) =>
                    {
                        if (batch.HasCallbacks())
                        {
                            await executeCallbacksAsync(c, tkn, batch, list).ConfigureAwait(false);
                        }
                        else
                        {
                            await c.ExecuteNonQueryAsync(tkn).ConfigureAwait(false);
                        }
                    }, token).ConfigureAwait(false);
                }

                if (list.Any())
                    throw new AggregateException(list);
            }
            finally
            {
                if (_writerPool != null)
                {
                    _writerPool?.Release(_writers);
                    _writers.Clear();
                }
            }
        }

        private static async Task executeCallbacksAsync(NpgsqlCommand cmd, CancellationToken tkn, BatchCommand batch,
            List<Exception> list)
        {
            using (var reader = await cmd.ExecuteReaderAsync(tkn).ConfigureAwait(false))
            {
                if (batch.Callbacks.Any())
                {
                    if (batch.Callbacks[0] != null)
                        await batch.Callbacks[0].PostprocessAsync(reader, list, tkn).ConfigureAwait(false);

                    for (var i = 1; i < batch.Callbacks.Count; i++)
                    {
                        if (!(batch.Calls[i - 1] is NoDataReturnedCall))
                        {
                            await reader.NextResultAsync(tkn).ConfigureAwait(false);
                        }



                        if (batch.Callbacks[i] != null)
                        {
                            await batch.Callbacks[i].PostprocessAsync(reader, list, tkn).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        public void Add(IEnumerable<IStorageOperation> operations)
        {
            foreach (var op in operations)
                Add(op);
        }

        public bool UseCharBufferPooling => _store.Options.UseCharBufferPooling;
        public string TenantId { get; }
    }
}