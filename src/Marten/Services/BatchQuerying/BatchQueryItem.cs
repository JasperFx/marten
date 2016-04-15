using System.Data.Common;
using System.Threading.Tasks;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public class BatchQueryItem<T> : IBatchQueryItem
    {
        private readonly IQueryHandler<T> _handler;

        public BatchQueryItem(IQueryHandler<T> handler)
        {
            _handler = handler;

            Completion = new TaskCompletionSource<T>();
        }


        public TaskCompletionSource<T> Completion { get; }
        public Task<T> Result => Completion.Task;

        public void Configure(IDocumentSchema schema, NpgsqlCommand command)
        {
            _handler.ConfigureCommand(command);
        }

        public void Read(DbDataReader reader, IIdentityMap map)
        {
            // TODO -- do async, all the way through
            var result = _handler.Handle(reader, map);
            Completion.SetResult(result);
        }
    }
}