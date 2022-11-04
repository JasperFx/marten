using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Events.Daemon;

namespace Marten.CommandLine.Commands.Projection;

public enum RebuildStatus
{
    NoData,
    Complete,
}

public interface IProjectionHost
{
    IReadOnlyList<IProjectionStore> AllStores();
    void ListenForUserTriggeredExit();
    Task<RebuildStatus> TryRebuildShards(IProjectionDatabase database, IReadOnlyList<AsyncProjectionShard> asyncProjectionShards, TimeSpan? shardTimeout=null);
    Task StartShards(IProjectionDatabase database, IReadOnlyList<AsyncProjectionShard> shards);
    Task WaitForExit();
}
