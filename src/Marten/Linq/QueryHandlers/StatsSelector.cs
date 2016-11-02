using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Linq.QueryHandlers
{
    internal class StatsSelector<T> : BasicSelector, ISelector<T>
    {
        private readonly ISelector<T> _inner;

        public StatsSelector(ISelector<T> inner) : base(inner.SelectFields().Concat(new[] { LinqConstants.StatsColumn }).ToArray())
        {
            _inner = inner;

            StartingIndex = _inner.SelectFields().Length;
        }

        public int StartingIndex { get; }

        public T Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            stats.TotalResults = reader.GetInt64(StartingIndex);

            return _inner.Resolve(reader, map, stats);
        }

        public async Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            stats.TotalResults = await reader.GetFieldValueAsync<long>(StartingIndex, token).ConfigureAwait(false);

            return _inner.Resolve(reader, map, stats);
        }
    }
}