using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Linq
{
    public class JsonSelector : BasicSelector, ISelector<string>
    {
        public JsonSelector() : base("data")
        {
        }

        public JsonSelector(string field) : base(field)
        {
            
        }

        public string Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return reader.GetString(0);
        }

        public Task<string> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return reader.GetFieldValueAsync<string>(0, token);
        }
    }
}