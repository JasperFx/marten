#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Sharded;

public class Bug4713Doc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// #4713 — ApplyAllConfiguredChangesToDatabaseAsync must be idempotent for per-tenant LIST partitions
/// when re-applied over the SAME store instance after tenants have been provisioned.
///
/// <para>
/// Root cause (a gap in the #4706 fix): the additive provisioning path
/// (AddTenantToShardAsync → ManagedListPartitions.additivelyMigrateTablesForNewPartitions) cleared
/// IgnorePartitionsInMigration on the per-mapping DocumentTable singleton and never restored it.
/// Because that Table instance is reused across shards and across applies, the #4706 short-circuit was
/// defeated on a later apply and the generic diff re-emitted CREATE TABLE … partition of for
/// already-existing per-tenant partitions → 42P07 (or re-triggered the destructive rebuild → 23514).
/// Fixed in Weasel 9.1.5 (save/restore the flag in the additive reconcile).
/// </para>
///
/// <para>
/// The distinguishing factor from <see cref="Bug_4706_sharded_partitioned_doc_rebuild"/> is that this
/// re-applies on the SAME store instance (so it sees the cleared singleton flag); #4706 built a fresh
/// store for its second deploy and so got a fresh flag.
/// </para>
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class Bug_4713_sharded_reapply_same_store: IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Bug_4713_sharded_reapply_same_store(ShardedPartitionedFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync("sharded"); } catch { }
        foreach (var connStr in _fixture.ConnectionStrings.Values)
        {
            await using var tenantConn = new NpgsqlConnection(connStr);
            await tenantConn.OpenAsync();
            try { await tenantConn.DropSchemaAsync("tenants"); } catch { }
            await ShardedPartitionedFixture.CleanMartenObjectsInPublicSchema(tenantConn);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DocumentStore BuildStore() => (DocumentStore)DocumentStore.For(opts =>
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

        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseTenantPartitionedEvents = true;

        opts.Schema.For<Bug4713Doc>()
            .MultiTenantedWithPartitioning(x => x.ByList())
            .Index(x => x.Name)
            .StartIndexesByTenantId();
    });

    [Fact]
    public async Task reapply_on_same_store_after_provisioning_is_idempotent()
    {
        var assignment = new Dictionary<string, string>
        {
            ["tA"] = _fixture.DbNames[0],
            ["tB"] = _fixture.DbNames[1],
            ["tC"] = _fixture.DbNames[2],
        };

        // ONE store instance for the whole test — this is the distinguishing factor from #4706.
        await using var store = BuildStore();

        var databases = await store.Options.Tenancy.BuildDatabases();
        foreach (var db in databases.OfType<IMartenDatabase>())
        {
            await db.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        // Provision tenants — the additive path that (pre-9.1.5) permanently cleared
        // IgnorePartitionsInMigration on the shared DocumentTable singleton.
        foreach (var (tenant, shard) in assignment)
        {
            await store.Advanced.AddTenantToShardAsync(tenant, shard, CancellationToken.None);
        }

        foreach (var tenant in assignment.Keys)
        {
            await using var session = store.LightweightSession(tenant);
            session.Store(new Bug4713Doc { Id = Guid.NewGuid(), Name = "n-" + tenant });
            await session.SaveChangesAsync();
        }

        // Capture the partition layout each shard has AFTER provisioning. #4713 is specifically about
        // re-apply IDEMPOTENCY, so this is the baseline a no-op re-apply must preserve.
        var shards = _fixture.ConnectionStrings.Keys.ToList();
        var before = new Dictionary<string, List<string>>();
        foreach (var shard in shards)
        {
            before[shard] = await partitionsOf(_fixture.ConnectionStrings[shard], "mt_doc_bug4713doc");
            _output.WriteLine($"[before][{shard}] {string.Join(", ", before[shard])}");
        }

        // Re-apply over the SAME store after provisioning. #4713: pre-fix the additive path had cleared
        // IgnorePartitionsInMigration on the shared DocumentTable singleton, so the generic diff was no
        // longer short-circuited and re-emitted CREATE TABLE … partition of for partitions the manager
        // knows about — changing the layout (and, in the reporter's value layout, throwing 42P07 when
        // re-creating an existing partition).
        var ex = await Record.ExceptionAsync(() =>
            store.Storage.ApplyAllConfiguredChangesToDatabaseAsync());

        if (ex != null)
        {
            _output.WriteLine(ex.ToString());
        }

        ex.ShouldBeNull("Re-applying on the same store after tenant provisioning must be idempotent " +
                        "(no 42P07 / no destructive rebuild)");

        // The re-apply must not have changed any shard's partition layout — that is the idempotency the
        // #4713 fix restores (flag stays set → ListPartitioning.CreateDelta → None on every shard).
        foreach (var shard in shards)
        {
            var after = await partitionsOf(_fixture.ConnectionStrings[shard], "mt_doc_bug4713doc");
            _output.WriteLine($"[after][{shard}] {string.Join(", ", after)}");
            after.ShouldBe(before[shard],
                $"re-applying the schema must not change shard {shard}'s per-tenant partitions (#4713)");
        }
    }

    private static async Task<List<string>> partitionsOf(string connStr, string parent)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        var rows = new List<string>();
        await using var reader = await conn.CreateCommand(
                "select c.relname from pg_inherits i join pg_class c on c.oid=i.inhrelid join pg_class p on p.oid=i.inhparent where p.relname = :parent")
            .With("parent", parent)
            .ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return rows;
    }
}
