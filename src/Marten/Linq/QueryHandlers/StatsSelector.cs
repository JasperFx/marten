using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Linq.QueryHandlers
{
    internal class StatsSelector<T> : BasicSelector, ISelector<T>
    {
        private readonly QueryStatistics _stats;
        private readonly ISelector<T> _inner;

        public StatsSelector(QueryStatistics stats, ISelector<T> inner) : base(inner.SelectFields().Concat(new[] { "count(1) OVER() as total_rows" }).ToArray())
        {
            _stats = stats;
            _inner = inner;

            StartingIndex = _inner.SelectFields().Length;
        }

        public int StartingIndex { get; }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            _stats.TotalResults = reader.GetInt64(StartingIndex);

            return _inner.Resolve(reader, map);
        }

        public async Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            _stats.TotalResults = await reader.GetFieldValueAsync<long>(StartingIndex, token).ConfigureAwait(false);

            return _inner.Resolve(reader, map);
        }
    }
}