#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon;

/// <summary>
///     Used internally by asynchronous projections.
/// </summary>
// This is public because it's used by the generated code
public interface IShardAgent
{
    CancellationToken Cancellation { get; }
    AgentStatus Status { get; }
    bool IsStopping { get; }
    ShardName Name { get; }
    ShardName ShardName { get; }
    long Position { get; }
    ShardExecutionMode Mode { get; set; }
    void StartRange(EventRange range);
    Task Stop(Exception? ex = null);
    Task Pause(TimeSpan timeout);
    Task TryAction(Func<Task> action, CancellationToken token, Action<ILogger, Exception>? logException = null,
        EventRangeGroup? group = null, GroupActionMode actionMode = GroupActionMode.Parent);

    Task<long> DrainSerializationFailureRecording();
}
