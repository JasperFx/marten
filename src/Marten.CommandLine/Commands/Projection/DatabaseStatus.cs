using Baseline;
using Marten.Events.Daemon;

namespace Marten.CommandLine.Commands.Projection
{
    internal class DatabaseStatus
    {
        public string Name { get; }
        public long HighWaterMark { get; set; }

        public readonly LightweightCache<string, CurrentShardState> Shards =
            new LightweightCache<string, CurrentShardState>(name => new CurrentShardState(name));

        public DatabaseStatus(string name)
        {
            Name = name;
        }


        public void ReadState(ShardState state)
        {
            if (state.ShardName == ShardState.HighWaterMark)
            {
                HighWaterMark = state.Sequence;
            }
            else
            {
                Shards[state.ShardName].ReadState(state);
            }
        }
    }
}