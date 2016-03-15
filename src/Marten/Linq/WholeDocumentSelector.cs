using System.Data.Common;
using Baseline;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public class WholeDocumentSelector<T> : ISelector<T>
    {
        private readonly IResolver<T> _resolver;

        public WholeDocumentSelector(IResolver<T> resolver)
        {
            _resolver = resolver;
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            return _resolver.Resolve(reader, map);
        }

        public string SelectClause(IDocumentMapping mapping)
        {
            return mapping.SelectFields().Join(", ");
        }
    }
}