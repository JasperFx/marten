using JasperFx;
using JasperFx.CommandLine;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// Inputs for the <c>daemonload-multinode-sharded</c> subcommand — the COORDINATOR role of the
/// marten#4883 native-HotCold SHARDED multi-node scenario (epic jasperfx#486 WS6). Provisions a
/// <c>MultiTenantedWithShardedDatabases</c> store over <c>--databases</c> shard databases, pools
/// <c>--tenants</c> tenants round-robin across them, launches <c>--nodes</c> HotCold daemon node
/// processes, appends continuously, and verifies that leadership of the shard databases is
/// DISTRIBUTED across the nodes (different nodes lead different shards), that killing a node
/// redistributes its shards to survivors, that every tenant catches up on its own shard, and that
/// each node's per-database connection footprint stays governed.
/// </summary>
public sealed class MultiNodeShardedInput: NetCoreInput
{
    [Description("Number of node processes to launch (native HotCold contention). Default: 2.")]
    public int NodesFlag { get; set; } = 2;

    [Description("Number of shard databases to pool tenants across on the same server. Leadership is negotiated per shard, so this is what makes agents distribute across nodes. Default: 2.")]
    public int DatabasesFlag { get; set; } = 2;

    [Description("Number of tenants to pool round-robin across the shard databases. Default: 40.")]
    public int TenantsFlag { get; set; } = 40;

    [Description("Number of async projections; each fans out one agent per tenant within each shard. Default: 2.")]
    public int ProjectionsFlag { get; set; } = 2;

    [Description("Seconds to keep the nodes running under continuous append load. Default: 90.")]
    public int DurationSecondsFlag { get; set; } = 90;

    [Description("Kill one node that currently leads at least one shard, this many seconds into the run, to exercise shard redistribution (0 = no kill). Default: 0.")]
    public int KillNodeAfterSecondsFlag { get; set; }

    [Description("Concurrent append writer tasks in the coordinator. Default: 4.")]
    public int WritersFlag { get; set; } = 4;

    [Description("Approximate total events appended per second across all writers. Default: 200.")]
    public int AppendRatePerSecondFlag { get; set; } = 200;

    [Description("pg_stat_activity sample interval in seconds. Default: 1.0.")]
    public double SampleSecondsFlag { get; set; } = 1.0;

    [Description("Seconds to wait after appends stop for every tenant's agents to catch up on every shard. Default: 90.")]
    public int CatchUpTimeoutSecondsFlag { get; set; } = 90;

    [Description("Fail (exit 1) if any single node's peak concurrent connections to a single shard database exceed this. WS6 expectation: per-node × per-database footprint stays governed (O(databases-led) per node). Default: 0 = report only.")]
    public int MaxConnectionsPerNodeShardFlag { get; set; }

    [Description("Fail (exit 1) if shard leadership does not span at least two distinct nodes at steady state. Off by default because a fast node can transiently grab every shard before the leadership poll rebalances. Default: false = report only.")]
    public bool RequireDistributionFlag { get; set; }

    [Description("Drop + recreate the dedicated schema and shard databases before the run. Default: false.")]
    public bool WipeFlag { get; set; }

    [Description("Optional path for a JSON metrics file.")]
    public string? MetricsFlag { get; set; }

    [Description("Optional path for a per-sample CSV connection trace (per node × database).")]
    public string? TraceFlag { get; set; }
}
