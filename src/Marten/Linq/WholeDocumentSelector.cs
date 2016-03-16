using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
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

        public Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return _resolver.ResolveAsync(reader, map, token);
        }

        public string SelectClause(IDocumentMapping mapping)
        {
            return mapping.SelectFields("d");
        }
    }
}