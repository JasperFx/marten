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

    /// <summary>Prefix for the per-server shard databases in the sharded multi-node scenario.</summary>
    public const string ShardDatabasePrefix = "scaletest_multinode_shard_";

    /// <summary>Deterministic shard database names — reconstructable by every node from just the count.</summary>
    public static string[] ShardDatabaseNames(int databaseCount) =>
        Enumerable.Range(0, Math.Max(1, databaseCount)).Select(i => $"{ShardDatabasePrefix}{i}").ToArray();

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

    /// <summary>
    /// Sharded variant for the marten#4883 sharded multi-node scenario: pools tenants across
    /// <paramref name="databaseCount"/> shard databases on the same server via
    /// <c>MultiTenantedWithShardedDatabases</c>. Every node builds a byte-identical sharded store
    /// (deterministic shard names from the count alone, one shared DaemonLockId) so native HotCold
    /// leadership is negotiated PER SHARD DATABASE — different nodes lead different shards, which is
    /// the cross-node distribution this scenario exercises.
    /// </summary>
    public static void ConfigureSharded(StoreOptions opts, int projectionCount, int databaseCount,
        string applicationName)
    {
        var shardNames = ShardDatabaseNames(databaseCount);
        var masterConnectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            ApplicationName = applicationName
        }.ConnectionString;

        opts.MultiTenantedWithShardedDatabases(x =>
        {
            x.ConnectionString = masterConnectionString;
            x.SchemaName = Schema;
            x.ApplicationName = applicationName;
            x.UseExplicitAssignment();

            foreach (var name in shardNames)
            {
                var shardConnectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
                {
                    Database = name,
                    ApplicationName = applicationName
                }.ConnectionString;
                x.AddDatabase(name, shardConnectionString);
            }
        });

        opts.DisableNpgsqlLogging = true;
        opts.DatabaseSchemaName = Schema;
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseTenantPartitionedEvents = true;
        opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        opts.Policies.AllDocumentsAreMultiTenanted();
        opts.Events.AddEventType<DaemonLoadEvent>();

        opts.Projections.DaemonLockId = DaemonLockId;
        opts.Projections.LeadershipPollingTime = 500;

        foreach (var name in ProjectionNames(projectionCount))
        {
            opts.Projections.Add(new DaemonLoadRollupProjection(name), ProjectionLifecycle.Async, name);
        }
    }
}
