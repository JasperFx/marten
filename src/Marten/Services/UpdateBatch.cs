using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Patching;
using Marten.Schema;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Services
{
    public class UpdateBatch : IDisposable
    {
        private readonly StoreOptions _options;
        private readonly ISerializer _serializer;
        private readonly Stack<BatchCommand> _commands = new Stack<BatchCommand>(); 
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        

        public UpdateBatch(StoreOptions options, ISerializer serializer, IManagedConnection connection, VersionTracker versions)
        {
            if (versions == null) throw new ArgumentNullException(nameof(versions));

            _options = options;
            _serializer = serializer;
            Versions = versions;

            _commands.Push(new BatchCommand(serializer));
            Connection = connection;
        }

        public ISerializer Serializer => _serializer;

        public BatchCommand Current()
        {
            return _lock.MaybeWrite(
                answer:() => _commands.Peek(), 
                missingTest: () => _commands.Peek().Count >= _options.UpdateBatchSize, 
                write:() => _commands.Push(new BatchCommand(_serializer))
            );
        }

        public VersionTracker Versions { get; }


        public void Add(IStorageOperation operation)
        {
            var batch = Current();

            operation.AddParameters(batch);

            batch.AddCall(operation, operation as ICallback);
        }

        public SprocCall Sproc(FunctionName function, ICallback callback = null)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));

            return Current().Sproc(function, callback);
        }

        public void Execute()
        {
            var list = new List<Exception>();

            foreach (var batch in _commands.ToArray())
            {
                var cmd = batch.BuildCommand();
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

            if (list.Any())
            {
                throw new AggregateException(list);
            }
        }

        private static void executeCallbacks(NpgsqlCommand cmd, BatchCommand batch, List<Exception> list)
        {
            using (var reader = cmd.ExecuteReader())
            {
                if (batch.Callbacks.Any())
                {
                    batch.Callbacks[0]?.Postprocess(reader, list);

                    for (int i = 1; i < batch.Callbacks.Count; i++)
                    {
                        reader.NextResult();

                        batch.Callbacks[i]?.Postprocess(reader, list);
                    }
                }
            }
        }

        public async Task ExecuteAsync(CancellationToken token)
        {
            var list = new List<Exception>();
            foreach (var batch in _commands.ToArray())
            {
                var cmd = batch.BuildCommand();
                await Connection.ExecuteAsync(cmd, async (c, tkn) =>
                {
                    if (batch.HasCallbacks())
                    {
                        await executeCallbacksAsync(c, tkn, batch, list);
                    }
                    else
                    {
                        await c.ExecuteNonQueryAsync(tkn);
                    }

                }, token).ConfigureAwait(false);
            }

            if (list.Any())
            {
                throw new AggregateException(list);
            }
        }

        private static async Task executeCallbacksAsync(NpgsqlCommand cmd, CancellationToken tkn, BatchCommand batch, List<Exception> list)
        {
            using (var reader = await cmd.ExecuteReaderAsync(tkn).ConfigureAwait(false))
            {
                if (batch.Callbacks.Any())
                {
                    if (batch.Callbacks[0] != null)
                    {
                        await batch.Callbacks[0].PostprocessAsync(reader, list, tkn).ConfigureAwait(false);
                    }

                    for (int i = 1; i < batch.Callbacks.Count; i++)
                    {
                        await reader.NextResultAsync(tkn).ConfigureAwait(false);

                        if (batch.Callbacks[i] != null)
                        {
                            await batch.Callbacks[i].PostprocessAsync(reader, list, tkn).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        public void Delete(TableName table, object id, NpgsqlDbType dbType)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            Current().Delete(table, id, dbType);
        }


        public IManagedConnection Connection { get; }

        public void DeleteWhere(TableName table, IWhereFragment @where)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (@where == null) throw new ArgumentNullException(nameof(@where));

            Current().DeleteWhere(table, @where);
        }

        public void Dispose()
        {
            Connection.Dispose();
        }

        public void Add(IEnumerable<IStorageOperation> operations)
        {
            foreach (var op in operations)
            {
                Add(op);
            }
        }
    }
}