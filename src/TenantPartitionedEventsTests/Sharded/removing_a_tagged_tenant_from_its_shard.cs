#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
///     <c>RemoveTenantFromShardAsync</c> drops the tenant's <c>mt_events</c> partition on its shard. Each DCB tag
///     table has a foreign key to <c>mt_events</c> that does not cascade, and the tag tables are not partitioned by
///     tenant, so the tenant's tag rows have to go before its partition does, as they do for
///     <c>DeleteAllTenantDataAsync</c> (#5694). Both tenants live on the same shard, so they share its tag table.
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class removing_a_tagged_tenant_from_its_shard: ShardedPartitionedContext
{
    private const string Removed = "rmtag_alpha";
    private const string Survivor = "rmtag_beta";

    public removing_a_tagged_tenant_from_its_shard(ShardedPartitionedFixture fixture): base(fixture)
    {
    }

    [Fact]
    public async Task removing_a_tenant_takes_its_tag_rows_and_leaves_another_tenants_alone()
    {
        var store = BuildShardedStore(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.AddEventType<ShardedTaggedRemovalEvent>();
            opts.Events.RegisterTagType<ShardedRemovalTag>("sharded_removal");
        });

        // The shard gets the full schema, the tag table included, before any tenant is registered
        var shard = Fixture.DbNames[0];
        var shardConnStr = Fixture.ConnectionStrings[shard];
        var shardDatabase = await store.Storage.FindOrCreateDatabase(shard);
        await shardDatabase.ApplyAllConfiguredChangesToDatabaseAsync(ct: TestContext.Current.CancellationToken);
        await store.Advanced.AddTenantToShardAsync(Removed, shard, CancellationToken.None);
        await store.Advanced.AddTenantToShardAsync(Survivor, shard, CancellationToken.None);

        var tag = new ShardedRemovalTag("probe-" + Guid.NewGuid().ToString("N"));
        await appendTaggedAsync(store, Removed, tag);
        var survivorStream = await appendTaggedAsync(store, Survivor, tag);

        await store.Advanced.RemoveTenantFromShardAsync(Removed, CancellationToken.None);

        (await tableExistsAsync(shardConnStr, $"mt_events_{Removed}")).ShouldBeFalse();
        (await countTagRowsAsync(shardConnStr, tag.Value, Removed)).ShouldBe(0);
        (await countTagRowsAsync(shardConnStr, tag.Value, Survivor))
            .ShouldBe(1, "removing one tenant must leave the other tenant's tag rows alone");

        await using var query = store.QuerySession(Survivor);
        (await query.Events.FetchStreamAsync(survivorStream, token: TestContext.Current.CancellationToken)).Count
            .ShouldBe(1);
    }

    private static async Task<Guid> appendTaggedAsync(IDocumentStore store, string tenantId, ShardedRemovalTag tag)
    {
        var streamId = Guid.NewGuid();
        await using var session = store.LightweightSession(tenantId);
        var e = session.Events.BuildEvent(new ShardedTaggedRemovalEvent());
        e.WithTag(tag);
        session.Events.StartStream(streamId, e);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return streamId;
    }

    private static async Task<bool> tableExistsAsync(string connectionString, string tableName)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand("select to_regclass(:name) is not null");
        cmd.Parameters.AddWithValue("name", $"public.{tableName}");
        return (bool)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<long> countTagRowsAsync(string connectionString, string value, string tenantId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand(
            "select count(*) from public.mt_event_tag_sharded_removal where value = :v and tenant_id = :t");
        cmd.Parameters.AddWithValue("v", value);
        cmd.Parameters.AddWithValue("t", tenantId);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}

public record ShardedRemovalTag(string Value);

public class ShardedTaggedRemovalEvent
{
}
