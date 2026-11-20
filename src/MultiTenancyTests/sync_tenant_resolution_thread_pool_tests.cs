using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.MultiTenancy;
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

    [Fact]
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

    // #5986. Red before the fix: every cold sharded tenant blocked a pool thread on the asynchronous shard lookup and
    // the #4942 partition repair. Now a tenant that already has a shard and its partitions resolves synchronously
    [Fact]
    public async Task a_burst_of_sync_session_opens_for_uncached_sharded_tenants_does_not_starve_the_thread_pool()
    {
        var callers = Math.Max(ThreadPool.ThreadCount, Environment.ProcessorCount) * 4 + 16;
        var tenantIds = Enumerable.Range(0, callers).Select(i => $"starve{i}").ToArray();

        await using var shards = await ScratchShards.CreateAsync(2);

        try
        {
            // Every tenant has a shard and its partitions, from an earlier store: a warm database
            await shards.ProvisionAsync(tenantIds);

            // A fresh store is a cold process, which has cached none of them
            await using var store = shards.BuildStore();

            // The store's one-off work first, as above, so the burst measures the per-tenant resolution only
            await shards.RunOneOffWorkAsync(store);

            var worst = await measureBurstAsync(tenantIds,
                tenantId =>
                {
                    using var session = store.LightweightSession(tenantId);
                    session.TenantId.ShouldBe(tenantId);
                });

            worst.ShouldBeLessThan(TimeSpan.FromMilliseconds(500),
                $"the thread pool was starved for {worst.TotalMilliseconds:N0} ms while {callers} callers opened sessions for uncached sharded tenants");
        }
        finally
        {
            await shards.DropMasterSchemaAsync();
        }
    }

    // #5985, weasel#839. Red before the fix: every cold tenant blocked a pool thread on the asynchronous existence
    // check of its database. Every tenant's database already exists, the steady state after a deploy: a warm server
    // and a cold process
    [Fact]
    public async Task a_burst_of_sync_session_opens_for_uncached_single_server_tenants_does_not_starve_the_thread_pool()
    {
        var callers = Math.Max(ThreadPool.ThreadCount, Environment.ProcessorCount) * 4 + 16;
        var prefix = "ss_starve_" + Guid.NewGuid().ToString("N")[..8] + "_";
        var databaseNames = Enumerable.Range(0, callers).Select(i => prefix + i).ToArray();

        await SingleServerDatabases.CreateAsync(databaseNames);

        try
        {
            await using (var store = DocumentStore.For(opts =>
                         {
                             opts.DisableNpgsqlLogging = true;
                             opts.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString);
                         }))
            {
                // One asynchronous resolution first, as above, so the burst measures the per-tenant lookup only
                await store.Options.Tenancy.GetTenantAsync(databaseNames[0]);

                var worst = await measureBurstAsync(databaseNames.Skip(1).ToArray(),
                    tenantId =>
                    {
                        using var session = store.LightweightSession(tenantId);
                        session.TenantId.ShouldBe(tenantId);
                        session.Database.Identifier.ShouldBe(tenantId);
                    });

                worst.ShouldBeLessThan(TimeSpan.FromMilliseconds(500),
                    $"the thread pool was starved for {worst.TotalMilliseconds:N0} ms while {callers} callers opened sessions for uncached single server tenants");
            }
        }
        finally
        {
            await SingleServerDatabases.DropAsync(databaseNames);
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

/// <summary>
///     #5985: the synchronous lookup of an uncached master-table tenant reads the control table with synchronous
///     I/O, which Npgsql refuses on a multiplexed data source. A multiplexed control database keeps the
///     asynchronous lookup instead of failing with <see cref="NotSupportedException" />.
/// </summary>
[Collection(TenantResolutionThreadPoolCollection.Name)]
public class sync_master_table_tenant_resolution_with_multiplexing
{
    [Fact]
    public async Task sync_session_for_an_uncached_tenant_works_when_the_control_database_is_multiplexed()
    {
        var schema = "mt_mux_" + Guid.NewGuid().ToString("N")[..10];
        var multiplexed = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            Multiplexing = true
        }.ConnectionString;

        await using var store = DocumentStore.For(opts =>
        {
            opts.DisableNpgsqlLogging = true;
            opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
            {
                x.ConnectionString = multiplexed;
                x.SchemaName = schema;
            });
        });

        try
        {
            await ((Marten.Storage.MasterTableTenancy)store.Options.Tenancy).TenantDatabase
                .ApplyAllConfiguredChangesToDatabaseAsync();
            await MasterTableRows.InsertAsync(schema, ["mux1", "mux2"]);

            // Synchronous, on a tenant nothing has resolved yet
            using (var session = store.LightweightSession("mux1"))
            {
                session.TenantId.ShouldBe("mux1");
            }

            store.Options.Tenancy.GetTenant("mux2").TenantId.ShouldBe("mux2");

            // And an unknown tenant is still refused the same way
            Should.Throw<UnknownTenantIdException>(() => store.Options.Tenancy.GetTenant("mux_missing"));
        }
        finally
        {
            await MasterTableRows.DropSchemaAsync(schema);
        }
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

internal static class SingleServerDatabases
{
    public static async Task CreateAsync(string[] databaseNames)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.AdminConnectionString);
        await conn.OpenAsync();

        foreach (var databaseName in databaseNames)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"create database {databaseName}";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public static async Task DropAsync(string[] databaseNames)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.AdminConnectionString);
        await conn.OpenAsync();

        foreach (var databaseName in databaseNames)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"drop database if exists {databaseName} with (force)";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
