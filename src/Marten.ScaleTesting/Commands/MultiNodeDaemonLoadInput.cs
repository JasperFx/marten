using JasperFx;
using JasperFx.CommandLine;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// Inputs for the <c>daemonload-multinode</c> subcommand — the COORDINATOR role of the
/// marten#4883 native-HotCold multi-node scenario (epic jasperfx#486 WS6). Provisions one
/// tenant-partitioned store, launches <c>--nodes</c> child processes of this same binary as
/// HotCold daemon nodes, appends continuously, samples per-node×database connections, optionally
/// kills the leader mid-run to exercise failover, and verifies per-tenant catch-up.
/// </summary>
public sealed class MultiNodeDaemonLoadInput: NetCoreInput
{
    [Description("Number of node processes to launch (native HotCold contention). Default: 2.")]
    public int NodesFlag { get; set; } = 2;

    [Description("Number of tenants to register on the partitioned store. Default: 50.")]
    public int TenantsFlag { get; set; } = 50;

    [Description("Number of async projections; each fans out one agent per tenant. Default: 2.")]
    public int ProjectionsFlag { get; set; } = 2;

    [Description("Seconds to keep the nodes running under continuous append load. Default: 90.")]
    public int DurationSecondsFlag { get; set; } = 90;

    [Description("Kill the current leader node this many seconds into the run to exercise leadership failover (0 = no failover). Default: 0.")]
    public int KillLeaderAfterSecondsFlag { get; set; }

    [Description("Concurrent append writer tasks in the coordinator. Default: 4.")]
    public int WritersFlag { get; set; } = 4;

    [Description("Approximate total events appended per second across all writers. Default: 200.")]
    public int AppendRatePerSecondFlag { get; set; } = 200;

    [Description("pg_stat_activity sample interval in seconds. Default: 1.0.")]
    public double SampleSecondsFlag { get; set; } = 1.0;

    [Description("Seconds to wait after appends stop for every tenant's agents to catch up. Default: 90.")]
    public int CatchUpTimeoutSecondsFlag { get; set; } = 90;

    [Description("Fail (exit 1) if any single node's peak concurrent connections to the store DB exceed this. The WS6 expectation is per-node footprint stays governed (O(databases) per node). Default: 0 = report only.")]
    public int MaxConnectionsPerNodeFlag { get; set; }

    [Description("Drop + recreate the dedicated schema before the run. Default: false.")]
    public bool WipeFlag { get; set; }

    [Description("Optional path for a JSON metrics file.")]
    public string? MetricsFlag { get; set; }

    [Description("Optional path for a per-sample CSV connection trace (per node × database).")]
    public string? TraceFlag { get; set; }
}
