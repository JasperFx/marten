using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon.Resiliency;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    public enum GroupActionMode
    {
        /// <summary>
        /// If the action is at the parent level, you can skip events
        /// and retry from here
        /// </summary>
        Parent,

        /// <summary>
        /// If the action is at the child level, the daemon error handling
        /// cannot skip events at this level, but needs to be retried
        /// from the parent action level
        /// </summary>
        Child
    }

    internal class ActionParameters
    {
        public ShardAgent Shard { get; }
        public Func<Task> Action { get; }
        public CancellationToken Cancellation { get; private set; }

        public GroupActionMode GroupActionMode { get; set; } = GroupActionMode.Parent;

        public ActionParameters(Func<Task> action, CancellationToken cancellation) : this(null, action, cancellation)
        {

        }

        public ActionParameters(ShardAgent shard, Func<Task> action) : this(shard, action, shard.Cancellation)
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

        public int Attempts { get; private set; } = 0;
        public TimeSpan Delay { get; private set; } = default;

        public Action<ILogger, Exception> LogAction { get; set; }
        public EventRangeGroup Group { get; set; }

        public void IncrementAttempts(TimeSpan delay = default)
        {
            Attempts++;
            Delay = delay;
            if (Group == null) return;

            Group.Reset();
            Cancellation = Group.Cancellation;
        }

        public void ApplySkip(SkipEvent skip)
        {
            Group?.SkipEventSequence(skip.Event.Sequence);

            // You have to reset the CancellationToken for the group
            Group?.Reset();

            Cancellation = Group?.Cancellation ?? Cancellation;


            // Basically saying that the attempts start over when we skip
            Attempts = 0;
            Delay = default;
        }
    }
}
