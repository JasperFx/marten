using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Selectors;

namespace Marten.Events.Daemon.Progress
{
    internal class ShardStateSelector : ISelector<ShardState>
    {
        public ShardState Resolve(DbDataReader reader)
        {
            var name = reader.GetFieldValue<string>(0);
            var sequence = reader.GetFieldValue<long>(1);
            return new ShardState(name, sequence);
        }

        public async Task<ShardState> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            var name = await reader.GetFieldValueAsync<string>(0, token);
            var sequence = await reader.GetFieldValueAsync<long>(1, token);
            return new ShardState(name, sequence);
        }
    }
}
