using JasperFx;
using JasperFx.CommandLine;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// Inputs for the <c>daemonload-node</c> subcommand — the WORKER role of the marten#4883
/// multi-node scenario. Not meant to be run by hand: the <c>daemonload-multinode</c> coordinator
/// launches N of these as separate OS processes (multi-node without cluster infra = multiple
/// processes of this same binary), each an <c>IHost</c> running
/// <c>AddAsyncDaemon(DaemonMode.HotCold)</c> against the shared tenant-partitioned store. The node
/// idles until its stdin closes or it receives a shutdown signal, so the coordinator can kill a
/// specific node mid-run to exercise leadership failover.
/// </summary>
public sealed class MultiNodeNodeInput: NetCoreInput
{
    [Description("Zero-based node index. Drives the per-node Application Name so pg_stat_activity attributes each node's connections. Required.")]
    public int NodeFlag { get; set; }

    [Description("Number of async projections to register. MUST match the coordinator's value so all nodes contend on an identical projection set. Default: 2.")]
    public int ProjectionsFlag { get; set; } = 2;
}
