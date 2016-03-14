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
    public class QueryResultsReader<T> : IDataReaderHandler
    {
        private readonly ISerializer _serializer;
        private readonly TaskCompletionSource<IList<T>> _source = new TaskCompletionSource<IList<T>>(); 

        public QueryResultsReader(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public Task<IList<T>> ReturnValue => _source.Task;

        public async Task Handle(DbDataReader reader, CancellationToken token)
        {
            var list = new List<T>();

            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var doc = _serializer.FromJson<T>(reader.GetString(0));
                list.Add(doc);
            }

            _source.SetResult(list);
        }
    }

    public class MultipleResultsReader<T> : IDataReaderHandler where T : class
    {
        private readonly TaskCompletionSource<IList<T>> _taskSource = new TaskCompletionSource<IList<T>>();
        private ISelector<T> _selector;
        private readonly IIdentityMap _map;

        public MultipleResultsReader(ISelector<T> selector, IIdentityMap map)
        {
            _selector = selector;
            _map = map;
        }

        public Task<IList<T>> ReturnValue => _taskSource.Task;

        public async Task Handle(DbDataReader reader, CancellationToken token)
        {
            var list = new List<T>();

            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var doc = _selector.Resolve(reader, _map);
                list.Add(doc);
            }

            _taskSource.SetResult(list);
        }
    }

    public class QueryHandler<T> : MultipleResultsReader<T> where T : class
    {
        public static ISelector<T> SelectorFromQuery(IDocumentStorage storage, DocumentQuery query, NpgsqlCommand command)
        {
            return query.ConfigureCommand<T>(storage, command);
        }

        public QueryHandler(IDocumentStorage storage, DocumentQuery query, NpgsqlCommand command, IIdentityMap map) : base(SelectorFromQuery(storage, query, command),map)
        {
        }
    }

}