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

        public string Resolve(DbDataReader reader, IIdentityMap map)
        {
            return reader.GetString(0);
        }

        public async Task<string> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return await reader.GetFieldValueAsync<string>(0, token);
        }
    }
}