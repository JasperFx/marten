using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Util;

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

        public void WriteSelectClause(CommandBuilder sql, IQueryableDocument mapping)
        {
            _inner.WriteSelectClause(sql, mapping);
            foreach (var @join in _joins)
            {
                sql.Append(" ");
                @join.AppendJoin(sql, "d", mapping);
            }
        }
    }
}