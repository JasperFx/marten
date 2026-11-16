#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Sharded;

public class Bug5986Doc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// #5986: the synchronous <c>ShardedTenancy.GetTenant</c> skips the #4942 first-sight repair for a tenant that is
/// already fully provisioned, and must still run it for one that is not. Under <c>UseTenantPartitionedEvents</c> the
/// repair also covers the tenant's <c>mt_events</c>/<c>mt_streams</c> partitions and its per-tenant event sequence,
/// so each of those is damaged here behind the store's back, one at a time.
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class Bug_5986_sync_tenant_resolution : ShardedPartitionedContext
{
    public Bug_5986_sync_tenant_resolution(ShardedPartitionedFixture fixture): base(fixture)
    {
    }

    private DocumentStore BuildStore()
    {
        return BuildShardedStore(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.AddEventType<ShardedTestEvent>();
            opts.Schema.For<Bug5986Doc>().MultiTenantedWithPartitioning(x => x.ByList());
        });
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("document partition dropped")]
    [InlineData("events partition detached")]
    [InlineData("event sequence dropped")]
    public async Task sync_tenant_resolution_repairs_only_what_is_missing(string damage)
    {
        var tenantId = "t5986_" + damage.Split(' ')[0];

        // "Process 1" provisions the tenant end to end on a warm database
        var store1 = BuildStore();
        foreach (var db in (await store1.Options.Tenancy.BuildDatabases()).OfType<IMartenDatabase>())
        {
            await db.ApplyAllConfiguredChangesToDatabaseAsync(ct: TestContext.Current.CancellationToken);
        }

        var dbId = await store1.Advanced.AddTenantToShardAsync(tenantId, CancellationToken.None);
        store1.Dispose();

        await damageAsync(dbId, tenantId, damage);
        var rowVersion = await registryRowVersionAsync(dbId, tenantId);

        // "Process 2" has not seen the tenant. Its one-off pool work runs first, without caching any tenant, so the
        // synchronous resolution below is the tenant's first sight
        var store2 = BuildStore();
        await ((ShardedTenancy)store2.Options.Tenancy).ListDatabasesAsync(CancellationToken.None);

        store2.Options.Tenancy.GetTenant(tenantId).Database.Identifier.ShouldBe(dbId);

        (await relationStateAsync(dbId, $"mt_doc_bug5986doc_{tenantId}")).ShouldBe("attached");
        (await relationStateAsync(dbId, $"mt_events_{tenantId}")).ShouldBe("attached");
        (await relationStateAsync(dbId, $"mt_streams_{tenantId}")).ShouldBe("attached");
        (await relationStateAsync(dbId, $"mt_events_sequence_{tenantId}")).ShouldBe("present");

        // The repair's registry upsert gives the row a new version, so an unmoved row version means the repair was
        // skipped, which it must be exactly when there was nothing to repair
        if (damage == "nothing")
        {
            (await registryRowVersionAsync(dbId, tenantId)).ShouldBe(rowVersion);
        }
        else
        {
            (await registryRowVersionAsync(dbId, tenantId)).ShouldNotBe(rowVersion);
        }

        // And the tenant can write documents and events from a synchronously opened session
        await using (var session = store2.LightweightSession(tenantId))
        {
            session.Store(new Bug5986Doc { Id = Guid.NewGuid(), Name = damage });
            session.Events.StartStream(Guid.NewGuid(), new ShardedTestEvent { Value = damage });
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    private async Task damageAsync(string dbId, string tenantId, string damage)
    {
        var sql = damage switch
        {
            "nothing" => null,
            "document partition dropped" => $"drop table public.mt_doc_bug5986doc_{tenantId}",
            "events partition detached" => $"alter table public.mt_events detach partition public.mt_events_{tenantId}",
            "event sequence dropped" => $"drop sequence public.mt_events_sequence_{tenantId}",
            _ => throw new ArgumentOutOfRangeException(nameof(damage))
        };

        if (sql == null) return;

        await using var conn = new NpgsqlConnection(Fixture.ConnectionStrings[dbId]);
        await conn.OpenAsync();
        await conn.CreateCommand(sql).ExecuteNonQueryAsync();
    }

    private async Task<string?> registryRowVersionAsync(string dbId, string tenantId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionStrings[dbId]);
        await conn.OpenAsync();
        return await conn
            .CreateCommand("select xmin::text from tenants.mt_tenant_partitions where partition_value = :id")
            .With("id", tenantId)
            .ExecuteScalarAsync() as string;
    }

    /// <summary>
    /// "attached" or "detached" for a table, "present" for anything else, "missing" when there is no such relation.
    /// </summary>
    private async Task<string> relationStateAsync(string dbId, string name)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionStrings[dbId]);
        await conn.OpenAsync();

        await using var reader = await conn
            .CreateCommand(
                "select c.relkind::text, c.relpartbound is not null from pg_class c join pg_namespace n on n.oid = c.relnamespace where n.nspname = 'public' and c.relname = :name")
            .With("name", name)
            .ExecuteReaderAsync();

        if (!await reader.ReadAsync()) return "missing";

        if (reader.GetString(0) is "r" or "p")
        {
            return reader.GetBoolean(1) ? "attached" : "detached";
        }

        return "present";
    }
}
