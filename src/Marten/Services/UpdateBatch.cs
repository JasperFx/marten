using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using NpgsqlTypes;

namespace Marten.Services
{
    public class UpdateBatch
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

        public BatchCommand Current()
        {
            return _lock.MaybeWrite(
                answer:() => _commands.Peek(), 
                missingTest: () => _commands.Peek().Count >= _options.UpdateBatchSize, 
                write:() => _commands.Push(new BatchCommand(_serializer))
            );
        }

        public BatchCommand.SprocCall Sproc(string name)
        {
            return Current().Sproc(name);
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
                await Connection.ExecuteAsync(batch.BuildCommand(), async (c, tkn) =>
                {
                    await c.ExecuteNonQueryAsync(tkn).ConfigureAwait(false);
                }, token);
            }
        }

        public void Delete(string tableName, object id, NpgsqlDbType dbType)
        {
            Current().Delete(tableName, id, dbType);
        }


        public IManagedConnection Connection { get; }
    }


}