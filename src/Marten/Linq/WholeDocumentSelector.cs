using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public class WholeDocumentSelector<T>: BasicSelector, ISelector<T>
    {
        private readonly IDocumentStorage<T> storage;

        public WholeDocumentSelector(IQueryableDocument mapping, IDocumentStorage<T> documentStorage)
            : base(mapping.SelectFields().Select(x => $"d.{x}").ToArray())
        {
            storage = documentStorage;
        }

        public T Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return storage.Resolve(0, reader, map);
        }

        public Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return storage.ResolveAsync(0, reader, map, token);
        }
    }
}
