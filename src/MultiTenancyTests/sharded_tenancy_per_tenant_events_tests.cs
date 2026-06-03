using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// #4598 — per-tenant event partitioning under sharded multi-tenancy. The headline bug
/// was that runtime-provisioned sharded tenants got the document LIST partitions but NOT
/// the per-tenant <c>mt_events_sequence_{suffix}</c> sequence, so the first event append
/// for such a tenant failed with <c>42P01: relation "{schema}.mt_events_sequence_{tenant}"
/// does not exist</c>. Fix: wire <c>PerTenantEventSequences.EnsureSequencesAsync</c> into
/// <c>ShardedTenancy.createPartitionsForTenant</c>; expose it via the jasperfx#413
/// <see cref="IDynamicTenantSource{T}.AddTenantAsync(string,CancellationToken)" />
/// auto-assign override so CritterWatch can provision store-agnostically.
/// </summary>
[Collection("sharded-tenancy")]
public class sharded_tenancy_per_tenant_events_tests : IAsyncLifetime
{
    private readonly ShardedTenancyFixture _fixture;
    private IDocumentStore _store = null!;

    public sharded_tenancy_per_tenant_events_tests(ShardedTenancyFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Clean schemas before each test — both the master pool DB and each shard.
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync("sharded"); } catch { }

        foreach (var connStr in _fixture.ConnectionStrings.Values)
        {
            await using var tenantConn = new NpgsqlConnection(connStr);
            await tenantConn.OpenAsync();
            try { await tenantConn.DropSchemaAsync("tenants"); } catch { }
            await ShardedTenancyFixture.cleanMartenObjectsInPublicSchema(tenantConn);
        }
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    private IDocumentStore CreateStore(Action<ShardedTenancyOptions>? customConfig = null, Action<StoreOptions>? storeConfig = null)
    {
        _store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = "sharded";
                x.PartitionSchemaName = "tenants";

                foreach (var (dbName, connStr) in _fixture.ConnectionStrings)
                {
                    x.AddDatabase(dbName, connStr);
                }

                customConfig?.Invoke(x);
            });

            opts.AutoCreateSchemaObjects = AutoCreate.All;

            // The config CritterWatch hits: conjoined events, quick-append,
            // per-tenant event partitioning. The docs at
            // martendb.io/events/multitenancy.html#per-tenant-event-partitioning
            // claim this composes with the sharded model.
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;

            opts.Events.AddEventType<ShardedTestEvent>();

            storeConfig?.Invoke(opts);
        });

        return _store;
    }

    // ---- Headline regression: append works after runtime provisioning ----

    [Fact]
    public async Task append_event_for_runtime_provisioned_tenant_does_not_throw_42P01()
    {
        CreateStore(x => x.UseSmallestDatabaseAssignment());

        // The documented provisioning entry point — succeeds today (creates document partitions)
        // but on master fails when the tenant later tries to append events.
        var dbId = await _store.Advanced.AddTenantToShardAsync("alpha", CancellationToken.None);
        dbId.ShouldNotBeNull();

        // The actual bug surface: append. On master this throws 42P01 because
        // quick-append calls nextval(mt_events_sequence_alpha) and the sequence was
        // never created. With the fix, it succeeds.
        await using var session = _store.LightweightSession("alpha");
        session.Events.StartStream(Guid.NewGuid(), new ShardedTestEvent { Value = "first" });
        await session.SaveChangesAsync();
    }

    // #4611 — With UseTenantPartitionedEvents, StartStream actions are routed through the bulk
    // mt_quick_append_events operation. Combined with UseMandatoryStreamTypeDeclaration, the
    // post-process guard that rejects appends to a non-existent stream (first event => version 1)
    // wrongly fired for a legitimate StartStream (also version 1), throwing NonExistentStreamException
    // and tombstoning the events. A later append to the (never-created) stream then failed too.
    [Fact]
    public async Task starting_then_appending_a_stream_works_with_mandatory_stream_type()
    {
        CreateStore(x => x.UseSmallestDatabaseAssignment(),
            opts =>
            {
                opts.Events.StreamIdentity = StreamIdentity.AsString;
                opts.Events.UseMandatoryStreamTypeDeclaration = true;
            });
        await _store.Advanced.AddTenantToShardAsync("india", CancellationToken.None);

        var streamId = Guid.NewGuid().ToString();

        await using (var session = _store.LightweightSession("india"))
        {
            session.Events.StartStream<ShardedAggregate>(streamId, new ShardedTestEvent { Value = "first" });
            await session.SaveChangesAsync();
        }

        await using (var query = _store.QuerySession("india"))
        {
            (await query.Events.FetchStreamAsync(streamId)).Count.ShouldBe(1);
        }

        await using (var session = _store.LightweightSession("india"))
        {
            session.Events.Append(streamId, new ShardedTestEvent { Value = "second" });
            await session.SaveChangesAsync();
        }

        await using (var query = _store.QuerySession("india"))
        {
            (await query.Events.FetchStreamAsync(streamId)).Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task per_tenant_event_sequence_lives_in_the_assigned_shard()
    {
        CreateStore(x => x.UseSmallestDatabaseAssignment());

        var dbId = await _store.Advanced.AddTenantToShardAsync("bravo", CancellationToken.None);
        dbId.ShouldNotBeNull();

        // mt_events_sequence_bravo must exist in the ASSIGNED shard (not the default DB,
        // not other shards). That placement is what the fix has to get right — calling
        // EnsureSequencesAsync against `Tenancy.Default.Database` would create the
        // sequence in the wrong place and the assigned shard would still fail on append.
        await assertSequenceExists(_fixture.ConnectionStrings[dbId], expected: "mt_events_sequence_bravo");

        foreach (var (otherDbName, otherConnStr) in _fixture.ConnectionStrings)
        {
            if (otherDbName == dbId) continue;
            await assertSequenceDoesNotExist(otherConnStr, "mt_events_sequence_bravo",
                $"the sequence must live ONLY in the assigned shard '{dbId}', not in '{otherDbName}'");
        }
    }

    [Fact]
    public async Task per_tenant_event_partition_also_created_in_assigned_shard()
    {
        CreateStore(x => x.UseSmallestDatabaseAssignment());

        var dbId = await _store.Advanced.AddTenantToShardAsync("charlie", CancellationToken.None);

        // Trigger the lazy schema apply on the assigned shard via a real append —
        // this is the same flow a production caller hits and what the regression
        // exercised. Sharded provisioning leaves the parent schema creation to the
        // first storage interaction with the shard, so without an actual write we
        // would only see the partition-management tables.
        await using (var session = _store.LightweightSession("charlie"))
        {
            session.Events.StartStream(Guid.NewGuid(), new ShardedTestEvent { Value = "for-partition-check" });
            await session.SaveChangesAsync();
        }

        // The original report observed that AddTenantToShardAsync created the document
        // LIST partitions but not the event partition. This pins both ends — event
        // partition + sequence — for the runtime-provisioning path.
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[dbId]);
        await conn.OpenAsync();

        var tables = await conn.ExistingTablesAsync();
        tables.Any(t => t.Name == "mt_events_charlie").ShouldBeTrue(
            $"mt_events_charlie partition must exist in shard '{dbId}'. Tables: {string.Join(", ", tables.Select(t => t.QualifiedName))}");
    }

    // ---- Sibling entry points hit the same one code path ----

    [Fact]
    public async Task explicit_AddTenantToShardAsync_provisions_the_event_sequence_too()
    {
        CreateStore();

        // Explicit-target overload — different ShardedTenancy entry (AssignTenantAsync)
        // but must converge on the same createPartitionsForTenant path so the fix covers
        // it too.
        await _store.Advanced.AddTenantToShardAsync("delta", _fixture.DbNames[1], CancellationToken.None);

        await assertSequenceExists(_fixture.ConnectionStrings[_fixture.DbNames[1]],
            expected: "mt_events_sequence_delta");
    }

    [Fact]
    public async Task IDynamicTenantSource_AddTenantAsync_returns_assigned_database_id_and_provisions()
    {
        // jasperfx#413: the store-agnostic auto-assign override. CritterWatch will call
        // this via IServiceProvider.AddTenantAsync(tenantId) without referencing the
        // concrete ShardedTenancy type.
        CreateStore(x => x.UseSmallestDatabaseAssignment());

        var source = (IDynamicTenantSource<string>)_store.Options.Tenancy;
        var dbId = await source.AddTenantAsync("echo", CancellationToken.None);

        dbId.ShouldBeOneOf(_fixture.DbNames);
        await assertSequenceExists(_fixture.ConnectionStrings[dbId], expected: "mt_events_sequence_echo");

        // And the append actually works — the full surface this enables.
        await using var session = _store.LightweightSession("echo");
        session.Events.StartStream(Guid.NewGuid(), new ShardedTestEvent { Value = "via-dynamic-source" });
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task IDynamicTenantSource_caller_supplied_overload_assigns_to_named_shard()
    {
        CreateStore();

        var source = (IDynamicTenantSource<string>)_store.Options.Tenancy;
        await source.AddTenantAsync("foxtrot", _fixture.DbNames[2]);

        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        (await sharded.FindDatabaseForTenantAsync("foxtrot", CancellationToken.None))
            .ShouldBe(_fixture.DbNames[2]);
        await assertSequenceExists(_fixture.ConnectionStrings[_fixture.DbNames[2]],
            expected: "mt_events_sequence_foxtrot");
    }

    // ---- DI registration (jasperfx#413 addendum) ----

    [Fact]
    public void IDynamicTenantSource_is_registered_in_the_container_when_tenancy_is_sharded()
    {
        // jasperfx#413: store-agnostic admin tools (CritterWatch) resolve via GetServices.
        // Mirror the existing IMasterTableMultiTenancy registration pattern, conditional
        // on the configured tenancy actually implementing the interface.
        var services = new ServiceCollection();
        services.AddMarten(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = "sharded";
                x.PartitionSchemaName = "tenants";
                foreach (var (dbName, connStr) in _fixture.ConnectionStrings)
                {
                    x.AddDatabase(dbName, connStr);
                }
            });
        });

        using var provider = services.BuildServiceProvider();
        var source = provider.GetService<IDynamicTenantSource<string>>();
        source.ShouldNotBeNull("ShardedTenancy implements IDynamicTenantSource<string>; the DI registration must surface it");
        source.ShouldBeOfType<ShardedTenancy>();
    }

    [Fact]
    public void IDynamicTenantSource_is_NOT_registered_when_tenancy_is_default()
    {
        // Conditional registration: a non-dynamic tenancy must keep
        // GetServices<IDynamicTenantSource<string>>() empty so the JasperFx admin
        // extensions' graceful-no-op behavior holds.
        var services = new ServiceCollection();
        services.AddMarten(opts => opts.Connection(ConnectionSource.ConnectionString));

        using var provider = services.BuildServiceProvider();
        provider.GetService<IDynamicTenantSource<string>>().ShouldBeNull();
    }

    // ---- helpers ----

    private static async Task assertSequenceExists(string connectionString, string expected)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        var exists = (long)(await conn.CreateCommand(
            "select count(*) from pg_sequences where schemaname = 'public' and sequencename = :n")
            .With("n", expected)
            .ExecuteScalarAsync())!;
        exists.ShouldBe(1L, $"sequence '{expected}' should exist in {connectionString}");
    }

    private static async Task assertSequenceDoesNotExist(string connectionString, string name, string because)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        var exists = (long)(await conn.CreateCommand(
            "select count(*) from pg_sequences where schemaname = 'public' and sequencename = :n")
            .With("n", name)
            .ExecuteScalarAsync())!;
        exists.ShouldBe(0L, because);
    }
}

public class ShardedAggregate
{
    public string Id { get; set; } = string.Empty;
    public int Count { get; set; }

    public void Apply(ShardedTestEvent _) => Count++;
}
