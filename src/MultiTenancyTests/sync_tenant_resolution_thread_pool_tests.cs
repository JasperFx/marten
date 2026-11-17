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

    // Red before Weasel's MasterTableTenancyBase.TryResolveTenant (weasel#838) was adopted: 86 synchronous session
    // opens for uncached tenants stalled a 10 ms heartbeat for 2.1-2.7 s on 18 processors
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

    /// <summary>
    ///     Runs the burst and answers how long the thread pool went without running a 10 ms heartbeat, counted on a
    ///     dedicated (non-pool) thread's clock.
    /// </summary>
    /// <remarks>
    ///     Measured against wall-clock time alone, the stall also counted time the whole process was not scheduled,
    ///     and a busy machine (the local gate runs other suites beside this one) took a clean run past 500 ms while
    ///     the pool was never starved. A dedicated thread is delayed by the machine as much as the pool heartbeat is,
    ///     and not at all by pool starvation, so the stall is the number of its ticks that land between two pool ticks.
    /// </remarks>
    private async Task<TimeSpan> measureBurstAsync(string[] tenantIds, Action<string> open)
    {
        using var stop = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();

        var poolTicks = new ConcurrentQueue<TimeSpan>();
        var dedicatedTicks = new ConcurrentQueue<TimeSpan>();
        var heartbeat = Task.Run(() => poolHeartbeatAsync(clock, poolTicks, stop.Token));
        var dedicated = new Thread(() => dedicatedHeartbeat(clock, dedicatedTicks, stop.Token)) { IsBackground = true };
        dedicated.Start();

        // Let the heartbeats settle into their loops before the callers arrive
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
        await heartbeat;
        dedicated.Join();

        var pool = poolTicks.ToArray();
        var reference = dedicatedTicks.ToArray();
        var worstWallClock = TimeSpan.Zero;
        var worstTicks = 0;
        for (var i = 1; i < pool.Length; i++)
        {
            var gap = pool[i] - pool[i - 1];
            if (gap > worstWallClock) worstWallClock = gap;

            var from = pool[i - 1];
            var to = pool[i];
            var ticks = reference.Count(x => x > from && x < to);
            if (ticks > worstTicks) worstTicks = ticks;
        }

        var stall = HeartbeatInterval * worstTicks;

        _output.WriteLine(
            $"{tenantIds.Length} callers on {Environment.ProcessorCount} processors: burst took {burst.TotalMilliseconds:N0} ms, " +
            $"worst pool heartbeat gap {worstWallClock.TotalMilliseconds:N0} ms wall clock, {stall.TotalMilliseconds:N0} ms on a dedicated thread's clock");

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures.Select(x => x.ToString())));

        return stall;
    }

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(10);

    private static async Task poolHeartbeatAsync(Stopwatch clock, ConcurrentQueue<TimeSpan> ticks,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            ticks.Enqueue(clock.Elapsed);
            await Task.Delay(HeartbeatInterval, CancellationToken.None);
        }

        ticks.Enqueue(clock.Elapsed);
    }

    private static void dedicatedHeartbeat(Stopwatch clock, ConcurrentQueue<TimeSpan> ticks, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            ticks.Enqueue(clock.Elapsed);
            Thread.Sleep(HeartbeatInterval);
        }
    }
}

/// <summary>
///     #5985: the synchronous master-table lookup (Weasel's <c>TryResolveTenant</c>, weasel#838) refuses and caches
///     exactly as the asynchronous one does, and a multiplexed control database, which Npgsql refuses synchronous
///     I/O on, keeps the asynchronous lookup instead of failing with <see cref="NotSupportedException" />.
/// </summary>
[Collection(TenantResolutionThreadPoolCollection.Name)]
public class sync_master_table_tenant_resolution
{
    [Fact]
    public async Task sync_lookup_refuses_and_caches_like_the_async_lookup()
    {
        var schema = "mt_sync_" + Guid.NewGuid().ToString("N")[..10];

        await using var store = DocumentStore.For(opts =>
        {
            opts.DisableNpgsqlLogging = true;
            opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = schema;
            });
        });

        var tenancy = (Marten.Storage.MasterTableTenancy)store.Options.Tenancy;

        try
        {
            await tenancy.TenantDatabase.ApplyAllConfiguredChangesToDatabaseAsync();
            await MasterTableRows.InsertAsync(schema, ["warm", "sync1", "off1"]);
            await MasterTableRows.DisableAsync(schema, "off1");

            // #6058: before anything asynchronous has run, the control table is provisioned synchronously
            // (weasel#850), so even the first lookup is synchronous
            store.Options.Tenancy.GetTenant("warm").TenantId.ShouldBe("warm");

            tenancy.TryResolveTenant("missing", out var unknown).ShouldBeTrue();
            unknown.Database.ShouldBeNull();

            var sync1 = store.Options.Tenancy.GetTenant("sync1");
            sync1.TenantId.ShouldBe("sync1");

            // One cache for both paths
            (await store.Options.Tenancy.GetTenantAsync("sync1")).Database.ShouldBeSameAs(sync1.Database);
            tenancy.AllDatabases().ShouldContain(sync1.Database);

            // Unknown and disabled tenants are refused as before, and nothing is cached for them
            Should.Throw<UnknownTenantIdException>(() => store.Options.Tenancy.GetTenant("missing"))
                .ShouldNotBeOfType<DisabledTenantException>();
            Should.Throw<DisabledTenantException>(() => store.Options.Tenancy.GetTenant("off1"));
            tenancy.FindCachedDatabase("missing").ShouldBeNull();
            tenancy.FindCachedDatabase("off1").ShouldBeNull();
        }
        finally
        {
            await MasterTableRows.DropSchemaAsync(schema);
        }
    }

    // #6058, the cold-process half of #5985 (weasel#850). Red before MasterTableTenancy overrode
    // TryProvisionControlTableSynchronously: a process whose first touch is synchronous declined every uncached
    // tenant until something asynchronous had provisioned the control table, and blocked a thread on the
    // asynchronous lookup for each of them
    [Fact]
    public async Task a_cold_process_resolves_its_first_tenant_synchronously()
    {
        var schema = "mt_cold_" + Guid.NewGuid().ToString("N")[..10];

        StoreOptions configure(StoreOptions opts)
        {
            opts.DisableNpgsqlLogging = true;
            opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = schema;
            });
            return opts;
        }

        try
        {
            // An earlier process built the control table and recorded the tenants
            await using (var earlier = DocumentStore.For(opts => configure(opts)))
            {
                await ((Marten.Storage.MasterTableTenancy)earlier.Options.Tenancy).TenantDatabase
                    .ApplyAllConfiguredChangesToDatabaseAsync();
            }

            await MasterTableRows.InsertAsync(schema, ["cold1", "cold2"]);

            // A fresh store is a cold process: nothing asynchronous has run in it
            await using var store = DocumentStore.For(opts => configure(opts));
            var tenancy = (Marten.Storage.MasterTableTenancy)store.Options.Tenancy;

            tenancy.TryResolveTenant("cold1", out var cold1).ShouldBeTrue();
            cold1.Database.ShouldNotBeNull();

            using (var session = store.LightweightSession("cold2"))
            {
                session.TenantId.ShouldBe("cold2");
            }

            tenancy.FindCachedDatabase("cold2").ShouldNotBeNull();
            Should.Throw<UnknownTenantIdException>(() => store.Options.Tenancy.GetTenant("cold_missing"));
        }
        finally
        {
            await MasterTableRows.DropSchemaAsync(schema);
        }
    }

    [Fact]
    public async Task a_cold_process_creates_a_missing_control_table_synchronously()
    {
        var schema = "mt_cold_" + Guid.NewGuid().ToString("N")[..10];

        await using var store = DocumentStore.For(opts =>
        {
            opts.DisableNpgsqlLogging = true;
            opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = schema;
            });
        });

        var tenancy = (Marten.Storage.MasterTableTenancy)store.Options.Tenancy;

        try
        {
            // No control table at all yet: the synchronous lookup creates it, then reads it
            tenancy.TryResolveTenant("nobody", out var nobody).ShouldBeTrue();
            nobody.Database.ShouldBeNull();

            // The same table the asynchronous migration builds: nothing is left for it to do
            await tenancy.TenantDatabase.AssertDatabaseMatchesConfigurationAsync();

            await MasterTableRows.InsertAsync(schema, ["created1"]);
            store.Options.Tenancy.GetTenant("created1").TenantId.ShouldBe("created1");
        }
        finally
        {
            await MasterTableRows.DropSchemaAsync(schema);
        }
    }

    [Fact]
    public async Task a_cold_process_does_not_create_the_control_table_when_auto_create_is_off()
    {
        var schema = "mt_cold_" + Guid.NewGuid().ToString("N")[..10];

        await using var store = DocumentStore.For(opts =>
        {
            opts.DisableNpgsqlLogging = true;
            opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = schema;
                x.AutoCreate = AutoCreate.None;
            });
        });

        var tenancy = (Marten.Storage.MasterTableTenancy)store.Options.Tenancy;

        try
        {
            // The operator owns the schema, so nothing is created, and the read fails as the asynchronous one does
            Should.Throw<PostgresException>(() => tenancy.TryResolveTenant("nobody", out _))
                .SqlState.ShouldBe(PostgresErrorCodes.UndefinedTable);

            (await controlTableExistsAsync(schema)).ShouldBeFalse();
        }
        finally
        {
            await MasterTableRows.DropSchemaAsync(schema);
        }
    }

    private static async Task<bool> controlTableExistsAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select to_regclass(@name) is not null";
        cmd.Parameters.AddWithValue("name", schema + ".mt_tenant_databases");
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

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
            await MasterTableRows.InsertAsync(schema, ["mux1", "mux2", "mux3"]);

            // Synchronous, on a tenant nothing has resolved yet, before anything asynchronous has run
            using (var session = store.LightweightSession("mux1"))
            {
                session.TenantId.ShouldBe("mux1");
            }

            // And after the control table is provisioned, when the synchronous lookup would otherwise be taken
            await store.Options.Tenancy.GetTenantAsync("mux2");
            store.Options.Tenancy.GetTenant("mux3").TenantId.ShouldBe("mux3");

            // An unknown tenant is still refused the same way
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

    public static async Task DisableAsync(string schema, string tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"update {schema}.mt_tenant_databases set disabled = true where tenant_id = @id";
        cmd.Parameters.AddWithValue("id", tenantId);
        await cmd.ExecuteNonQueryAsync();
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
