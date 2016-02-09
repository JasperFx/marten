using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public class MultipleResultsReader<T> : IDataReaderHandler where T : class
    {
        private readonly TaskCompletionSource<IList<T>> _taskSource = new TaskCompletionSource<IList<T>>();
        private readonly IDocumentStorage _storage;
        private readonly IIdentityMap _map;

        public MultipleResultsReader(IDocumentStorage storage, IIdentityMap map)
        {
            _storage = storage;
            _map = map;
        }

        public Task<IList<T>> ReturnValue => _taskSource.Task;

        public async Task Handle(DbDataReader reader, CancellationToken token)
        {
            var list = new List<T>();
            var resolver = _storage.As<IResolver<T>>();

            while (await reader.ReadAsync(token))
            {
                var doc = resolver.Resolve(reader, _map);
                list.Add(doc);
            }

            _taskSource.SetResult(list);
        }
    }

    public class QueryHandler<T> : MultipleResultsReader<T> where T : class
    {
        public QueryHandler(IDocumentStorage storage, IIdentityMap map) : base(storage, map)
        {
        }


        public void Configure(NpgsqlCommand command, DocumentQuery query)
        {
            query.ConfigureCommand(command);
        }

        

    }

}