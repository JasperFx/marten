using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Selectors;

namespace Marten.Events.Daemon.Progress
{
    public class ProjectionProgressSelector : ISelector<ProjectionProgress>
    {
        public ProjectionProgress Resolve(DbDataReader reader)
        {
            return new ProjectionProgress
            {
                ProjectionOrShardName = reader.GetFieldValue<string>(0),
                LastSequenceId = reader.GetFieldValue<long>(1)
            };
        }

        public async Task<ProjectionProgress> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            return new ProjectionProgress
            {
                ProjectionOrShardName = await reader.GetFieldValueAsync<string>(0, token),
                LastSequenceId = await reader.GetFieldValueAsync<long>(1, token)
            };
        }
    }
}