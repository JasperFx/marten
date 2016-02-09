using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;

namespace Marten.Services.BatchQuerying
{
    public class MultipleResultsReader<T> : IDataReaderHandler where T : class
    {
        private readonly TaskCompletionSource<IList<T>> _taskSource;
        private readonly IDocumentStorage _storage;
        private readonly IIdentityMap _map;

        public MultipleResultsReader(TaskCompletionSource<IList<T>> taskSource, IDocumentStorage storage, IIdentityMap map)
        {
            _taskSource = taskSource;
            _storage = storage;
            _map = map;
        }

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
}