using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Microsoft.CodeAnalysis.Emit;
using NpgsqlTypes;

namespace Marten.Services
{
    public class UpdateBatch
    {
        private readonly StoreOptions _options;
        private readonly ISerializer _serializer;
        private readonly ICommandRunner _runner;
        private readonly Stack<BatchCommand> _commands = new Stack<BatchCommand>(); 
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        

        public UpdateBatch(StoreOptions options, ISerializer serializer, ICommandRunner runner)
        {
            _options = options;
            _serializer = serializer;
            _runner = runner;

            _commands.Push(new BatchCommand(serializer));
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
            // TODO -- come back to this for transactional boundaries
            foreach (var batch in _commands.ToArray())
            {
                _runner.Execute(batch.BuildCommand(), c => c.ExecuteNonQuery());
            }
        }

        public async Task ExecuteAsync(CancellationToken token)
        {
            // TODO -- come back to this for transactional boundaries
            foreach (var batch in _commands.ToArray())
            {
                await _runner.ExecuteAsync(batch.BuildCommand(), async (c, tkn) =>
                {
                    await c.ExecuteNonQueryAsync(tkn).ConfigureAwait(false);
                }, token);
            }
        }

        public void Delete(string tableName, object id, NpgsqlDbType dbType)
        {
            Current().Delete(tableName, id, dbType);
        }




    }


}