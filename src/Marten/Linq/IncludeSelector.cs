using System.Data.Common;
using System.Linq;
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

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            return _inner.Resolve(reader, map);
        }

        public string[] SelectFields()
        {
            return _inner.SelectFields();
        }

        public string ToSelectClause(IDocumentMapping mapping)
        {
            var select = _inner.ToSelectClause(mapping);
            return $"{select} {_joins.Select(x => x.JoinText).Join(" ")}";
        }
    }
}