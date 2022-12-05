using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon.Resiliency;
using Marten.Storage;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon;

internal class ActionParameters
{
    public ActionParameters(Func<Task> action, CancellationToken cancellation): this(null, action, cancellation)
    {
    }

    public ActionParameters(ShardAgent shard, Func<Task> action): this(shard, action, shard.Cancellation)
    {
    }

    public ActionParameters(ShardAgent shard, Func<Task> action, CancellationToken cancellation)
    {
        Cancellation = cancellation;

        Shard = shard;
        Action = action;

        LogAction = (logger, ex) =>
        {
            logger.LogError(ex, "Error in Async Projection '{ShardName}' / '{Message}'", Shard.ShardName.Identity,
                ex.Message);
        };
    }

    public ShardAgent Shard { get; }
    public Func<Task> Action { get; }
    public CancellationToken Cancellation { get; private set; }

    public GroupActionMode GroupActionMode { get; set; } = GroupActionMode.Parent;

    public int Attempts { get; private set; }
    public TimeSpan Delay { get; private set; }

    public Action<ILogger, Exception> LogAction { get; set; }
    public EventRangeGroup Group { get; set; }

    public void IncrementAttempts(TimeSpan delay = default)
    {
        Attempts++;
        Delay = delay;
        if (Group == null)
        {
            return;
        }

        Group.Reset();
        Cancellation = Group.Cancellation;
    }

    public async Task ApplySkipAsync(SkipEvent skip, IMartenDatabase database)
    {
        if (Group != null)
        {
            await Group.SkipEventSequence(skip.Event.Sequence, database).ConfigureAwait(false);

            // You have to reset the CancellationToken for the group
            Group.Reset();
            Cancellation = Group.Cancellation;
        }

        // Basically saying that the attempts start over when we skip
        Attempts = 0;
        Delay = default;
    }
}
