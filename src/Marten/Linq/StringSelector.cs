using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Linq
{
    public class StringSelector : BasicSelector, ISelector<string>
    {
        public StringSelector() : base("data")
        {
        }

        public string Resolve(DbDataReader reader, IIdentityMap map)
        {
            return reader.GetString(0);
        }

        public Task<string> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return reader.GetFieldValueAsync<string>(0, token);
        }
    }
}