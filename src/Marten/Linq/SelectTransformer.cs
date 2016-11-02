using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public class SelectTransformer<T> : BasicSelector, ISelector<T>
    {
        public SelectTransformer(IQueryableDocument mapping, TargetObject target) 
            : base(target.ToSelectField(mapping))
        {
        }

        public SelectTransformer(IQueryableDocument mapping, TargetObject target, bool distinct)
            : base(distinct, target.ToSelectField(mapping)) {
        }

        public T Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            var json = reader.GetString(0);
            return map.Serializer.FromJson<T>(json);
        }

        public async Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var json = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            return map.Serializer.FromJson<T>(json);
        }
    }
}