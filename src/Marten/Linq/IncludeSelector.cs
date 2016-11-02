using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Includes;

namespace Marten.Linq
{
    public class IncludeSelector<T> : ISelector<T>
    {
        private readonly IIncludeJoin[] _joins;
        private readonly ISelector<T> _inner;

        public IncludeSelector(IDocumentSchema schema, ISelector<T> inner, IIncludeJoin[] joins)
        {
            _joins = joins;
            var selector = inner;

            joins.Each(x => selector = x.WrapSelector(schema, selector));

            _inner = selector;
        }

        public T Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return _inner.Resolve(reader, map, stats);
        }

        public Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return _inner.ResolveAsync(reader, map, stats, token);
        }

        public string[] SelectFields()
        {
            return _inner.SelectFields();
        }

        public string ToSelectClause(IQueryableDocument mapping)
        {
            var select = _inner.ToSelectClause(mapping);
            return $"{select} {_joins.Select(x => x.JoinText).Join(" ")}";
        }
    }
}