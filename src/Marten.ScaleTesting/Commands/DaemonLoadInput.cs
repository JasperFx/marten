using JasperFx;
using JasperFx.CommandLine;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// Inputs for the <c>daemonload</c> subcommand (jasperfx#486 WS2 measurement). A RUNNING-daemon
/// scenario, as opposed to the rebuild-centric rest of the harness: N tenants under
/// <c>UseTenantPartitionedEvents</c>, per-tenant subscription agents fanned out by the daemon,
/// continuous appends across every tenant, and <c>pg_stat_activity</c> sampled throughout for
/// the store's connection footprint. The WS2 question this answers: do steady-state connections
/// scale O(databases) or O(tenant agents)?
/// </summary>
public sealed class DaemonLoadInput: NetCoreInput
{
    [Description("Number of tenants to register on the partitioned store. Default: 100.")]
    public int TenantsFlag { get; set; } = 100;

    [Description("Number of async projections to register — each fans out one agent per tenant. Default: 2.")]
    public int ProjectionsFlag { get; set; } = 2;

    [Description("Seconds to keep the daemon running under continuous append load. Default: 120.")]
    public int DurationSecondsFlag { get; set; } = 120;

    [Description("Concurrent append writer tasks. Default: 4.")]
    public int WritersFlag { get; set; } = 4;

    [Description("Approximate total events appended per second across all writers. Default: 200.")]
    public int AppendRatePerSecondFlag { get; set; } = 200;

    [Description("pg_stat_activity sample interval in seconds. Default: 1.0.")]
    public double SampleSecondsFlag { get; set; } = 1.0;

    [Description("Seconds to wait after appends stop for every tenant's agents to catch up to that tenant's ceiling. Default: 60.")]
    public int CatchUpTimeoutSecondsFlag { get; set; } = 60;

    [Description("Fail (exit 1) if the store's peak concurrent connections exceed this. Default: 0 = report only.")]
    public int MaxConnectionsFlag { get; set; }

    [Description("Drop + recreate the dedicated schema before the run. Default: false.")]
    public bool WipeFlag { get; set; }

    [Description("Optional path for a JSON metrics file (peak/mean connections, throughput, catch-up).")]
    public string? MetricsFlag { get; set; }

    [Description("Optional path for a per-sample CSV connection trace.")]
    public string? TraceFlag { get; set; }
}
