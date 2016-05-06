using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using NpgsqlTypes;

namespace Marten.Services
{
    public class UpdateBatch : IDisposable
    {
        private readonly StoreOptions _options;
        private readonly ISerializer _serializer;
        private readonly Stack<BatchCommand> _commands = new Stack<BatchCommand>(); 
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        

        public UpdateBatch(StoreOptions options, ISerializer serializer, IManagedConnection connection)
        {
            _options = options;
            _serializer = serializer;

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

        public BatchCommand.SprocCall Sproc(FunctionName function)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));

            return Current().Sproc(function);
        }

        public void Execute()
        {
            foreach (var batch in _commands.ToArray())
            {
                Connection.Execute(batch.BuildCommand(), c => c.ExecuteNonQuery());
            }
        }

        public async Task ExecuteAsync(CancellationToken token)
        {
            foreach (var batch in _commands.ToArray())
            {
                await Connection.ExecuteAsync(batch.BuildCommand(), (c, tkn) => c.ExecuteNonQueryAsync(tkn), token).ConfigureAwait(false);
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
    }
}