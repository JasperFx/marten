using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace MultiTenancyTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public class TenantResolutionThreadPoolCollection
{
    public const string Name = "tenant-resolution-thread-pool";
}

/// <summary>
///     #5985: the synchronous session factories resolve a tenant that is not cached yet through
///     <c>ITenancy.GetTenant</c>. That used to block a pool thread on the asynchronous lookup, which itself
///     needed a free pool thread to finish: the starvation shape of #5969. A burst of synchronous session
///     opens for tenants nobody had resolved yet, after a deploy, stalled every other piece of work in the
///     process until the thread pool had injected enough threads.
/// </summary>
[Collection(TenantResolutionThreadPoolCollection.Name)]
public class sync_tenant_resolution_thread_pool_tests
{
    private readonly ITestOutputHelper _output;

    public sync_tenant_resolution_thread_pool_tests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Red on master: 86 synchronous session opens for uncached tenants stalled a 10 ms heartbeat for 2.1-2.7 s on
    // 18 processors. The 9.0 branch fixes this in Marten's own MasterTableTenancy; on master the tenant cache
    // belongs to Weasel's MasterTableTenancyBase, which has no synchronous way to resolve and cache a tenant yet
    [Fact(Skip = "#5985: needs a synchronous resolve on Weasel's MasterTableTenancyBase, JasperFx/weasel#838")]
    public async Task a_burst_of_sync_session_opens_for_uncached_master_table_tenants_does_not_starve_the_thread_pool()
    {
        var schema = "mt_starve_" + Guid.NewGuid().ToString("N")[..10];

        // More callers than the pool has threads, so the pool cannot give each of them a thread without
        // injecting more. Each one blocks a pool thread in OpenSession(), as application code does
        var callers = Math.Max(ThreadPool.ThreadCount, Environment.ProcessorCount) * 4 + 16;
        var tenantIds = Enumerable.Range(0, callers).Select(i => $"starve{i}").ToArray();

        await using var store = DocumentStore.For(opts =>
        {
            opts.DisableNpgsqlLogging = true;
            opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = schema;
            });
        });

        try
        {
            // Build the control table, then record every tenant behind the store's back, so none of them
            // is cached in this store. Every tenant points at the test database: the lookup is what is
            // being measured, not the tenant databases
            await ((Marten.Storage.MasterTableTenancy)store.Options.Tenancy).TenantDatabase
                .ApplyAllConfiguredChangesToDatabaseAsync();
            await MasterTableRows.InsertAsync(schema, tenantIds);

            // One asynchronous resolution first, so the one-off control-table check is behind us and the
            // burst measures the per-tenant lookup only
            await store.Options.Tenancy.GetTenantAsync(tenantIds[0]);

            var worst = await measureBurstAsync(tenantIds.Skip(1).ToArray(),
                tenantId =>
                {
                    using var session = store.LightweightSession(tenantId);
                    session.TenantId.ShouldBe(tenantId);
                });

            worst.ShouldBeLessThan(TimeSpan.FromMilliseconds(500),
                $"the thread pool was starved for {worst.TotalMilliseconds:N0} ms while {callers} callers opened sessions for uncached tenants");
        }
        finally
        {
            await MasterTableRows.DropSchemaAsync(schema);
        }
    }

    private async Task<TimeSpan> measureBurstAsync(string[] tenantIds, Action<string> open)
    {
        using var stop = new CancellationTokenSource();
        var heartbeat = Task.Run(() => measureWorstHeartbeatDelayAsync(stop.Token));

        // Let the heartbeat settle into its loop before the callers arrive
        await Task.Delay(100);

        var failures = new ConcurrentBag<Exception>();
        var stopwatch = Stopwatch.StartNew();

        var tasks = tenantIds.Select(tenantId => Task.Run(() =>
        {
            try
            {
                open(tenantId);
            }
            catch (Exception e)
            {
                failures.Add(e);
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        var burst = stopwatch.Elapsed;

        stop.Cancel();
        var worst = await heartbeat;

        _output.WriteLine(
            $"{tenantIds.Length} callers on {Environment.ProcessorCount} processors: burst took {burst.TotalMilliseconds:N0} ms, " +
            $"worst heartbeat delay {worst.TotalMilliseconds:N0} ms");

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures.Select(x => x.ToString())));

        return worst;
    }

    private static async Task<TimeSpan> measureWorstHeartbeatDelayAsync(CancellationToken token)
    {
        var worst = TimeSpan.Zero;
        var interval = TimeSpan.FromMilliseconds(10);
        var clock = Stopwatch.StartNew();

        while (!token.IsCancellationRequested)
        {
            var before = clock.Elapsed;
            await Task.Delay(interval, CancellationToken.None);
            var late = clock.Elapsed - before - interval;
            if (late > worst)
            {
                worst = late;
            }
        }

        return worst;
    }
}

internal static class MasterTableRows
{
    /// <summary>
    ///     Record tenants in the control table behind the store's back, so none of them is cached. Every one
    ///     points at the test database.
    /// </summary>
    public static async Task InsertAsync(string schema, string[] tenantIds)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        foreach (var tenantId in tenantIds)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"insert into {schema}.mt_tenant_databases (tenant_id, connection_string) values (@id, @cs)";
            cmd.Parameters.AddWithValue("id", tenantId);
            cmd.Parameters.AddWithValue("cs", ConnectionSource.ConnectionString);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public static async Task DropSchemaAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"drop schema if exists {schema} cascade";
        await cmd.ExecuteNonQueryAsync();
    }
}
