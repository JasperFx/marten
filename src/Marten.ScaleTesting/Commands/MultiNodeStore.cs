using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Storage;
using Npgsql;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// Shared store configuration for the marten#4883 multi-node daemonload scenario (epic
/// jasperfx#486 WS6). The coordinator process and every node process MUST build a byte-identical
/// store (same schema, same tenancy, same projection set, same daemon lock id) so they contend
/// over one native-HotCold leadership lock. Centralised here so the two roles can't drift.
/// </summary>
internal static class MultiNodeStore
{
    public const string Schema = "scaletest_multinode";
    public const string ApplicationBase = "scaletest-multinode";

    /// <summary>The shared advisory-lock id every node contends on for HotCold leadership.</summary>
    public const int DaemonLockId = 48620;

    public static string[] ProjectionNames(int count) =>
        Enumerable.Range(0, Math.Max(1, count)).Select(i => $"NodeRollup{i}").ToArray();

    public static string[] TenantIds(int count) =>
        Enumerable.Range(0, count).Select(i => $"tenant_{i:0000}").ToArray();

    /// <summary>Per-process Application Name so pg_stat_activity attributes connections to a node.</summary>
    public static string ApplicationNameForNode(int nodeIndex) => $"{ApplicationBase}-node{nodeIndex}";

    public static void Configure(StoreOptions opts, int projectionCount, string applicationName)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            ApplicationName = applicationName
        }.ConnectionString;

        opts.Connection(connectionString);
        opts.DisableNpgsqlLogging = true;
        opts.DatabaseSchemaName = Schema;
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseTenantPartitionedEvents = true;
        opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        opts.Policies.AllDocumentsAreMultiTenanted();
        opts.Events.AddEventType<DaemonLoadEvent>();

        // Tight leadership polling so failover redistribution is observable within a load run
        // rather than after a multi-second lease.
        opts.Projections.DaemonLockId = DaemonLockId;
        opts.Projections.LeadershipPollingTime = 500;

        foreach (var name in ProjectionNames(projectionCount))
        {
            opts.Projections.Add(new DaemonLoadRollupProjection(name), ProjectionLifecycle.Async, name);
        }
    }
}
