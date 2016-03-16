using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;

namespace Marten.Services.BatchQuerying
{
    public class SingleResultReader<T> : IDataReaderHandler where T : class
    {
        private readonly TaskCompletionSource<T> _taskSource;
        private readonly IDocumentStorage _storage;
        private readonly IIdentityMap _map;

        public SingleResultReader(TaskCompletionSource<T> taskSource, IDocumentStorage storage, IIdentityMap map)
        {
            _taskSource = taskSource;
            _storage = storage;
            _map = map;
        }

        public async Task Handle(DbDataReader reader, CancellationToken token)
        {
            var hasRecord = await reader.ReadAsync(token).ConfigureAwait(false);
            if (!hasRecord)
            {
                _taskSource.SetResult(null);
            }

            var doc = await _storage.As<IResolver<T>>().ResolveAsync(reader, _map, token).ConfigureAwait(false);
            _taskSource.SetResult(doc);
        }
    }
}