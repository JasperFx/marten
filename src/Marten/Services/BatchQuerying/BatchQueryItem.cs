using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public class BatchQueryItem<T> : IBatchQueryItem
    {
        private readonly IQueryHandler<T> _handler;

        public BatchQueryItem(IQueryHandler<T> handler, QueryStatistics stats)
        {
            _handler = handler;

            Completion = new TaskCompletionSource<T>();
            Stats = stats;
        }

        public QueryStatistics Stats { get; }


        public TaskCompletionSource<T> Completion { get; }
        public Task<T> Result => Completion.Task;

        public IQueryHandler Handler => _handler;

        public async Task Read(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var result = await _handler.HandleAsync(reader, map, Stats, token).ConfigureAwait(false);
            Completion.SetResult(result);
        }

        public void Read(DbDataReader reader, IIdentityMap map)
        {
            var result = _handler.Handle(reader, map, Stats);
            Completion.SetResult(result);
        }

    }
}