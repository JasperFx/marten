using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public class WholeDocumentSelector<T> : BasicSelector, ISelector<T>
    {
        private readonly IResolver<T> _resolver;

        public WholeDocumentSelector(IDocumentMapping mapping, IResolver<T> resolver)
            : base(mapping.SelectFields().Select(x => $"d.{x}").ToArray())
        {
            _resolver = resolver;
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            return _resolver.Resolve(0, reader, map);
        }

        public Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return _resolver.ResolveAsync(0, reader, map, token);
        }
    }
}