using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public class SingleFieldSelector<T>: BasicSelector, ISelector<T>
    {
        public SingleFieldSelector(IQueryableDocument mapping, MemberInfo[] members)
            : base(mapping.FieldFor(members).TypedLocator)
        {
        }

        public SingleFieldSelector(IQueryableDocument mapping, MemberInfo[] members, bool distinct)
            : base(distinct, mapping.FieldFor(members).TypedLocator)
        {
        }

        public SingleFieldSelector(bool distinct, string field) : base(distinct, field)
        {
        }

        public T Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return reader.IsDBNull(0) ? default : reader.GetFieldValue<T>(0);
        }

        public Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return reader.GetFieldValueAsync<T>(0, token);
        }
    }
}
