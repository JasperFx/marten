using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
#nullable enable
namespace Marten.Events.Daemon
{
    /// <summary>
    /// Used internally by asynchronous projections.
    /// </summary>
    // This is public because it's used by the generated code
    public interface IShardAgent
    {
        void StartRange(EventRange range);

        Task TryAction(Func<Task> action, CancellationToken token, Action<ILogger, Exception>? logException = null, EventRangeGroup? group = null, GroupActionMode actionMode = GroupActionMode.Parent);

        ShardName Name { get; }
        ShardExecutionMode Mode { get; set; }
    }
}
