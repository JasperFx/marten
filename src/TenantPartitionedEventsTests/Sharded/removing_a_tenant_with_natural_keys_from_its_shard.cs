#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
///     #5742 for the sharded removal path. <c>RemoveTenantFromShardAsync</c> drops the tenant's <c>mt_streams</c>
///     partition on its shard. A <c>[NaturalKey]</c> aggregate's <c>mt_natural_key_*</c> table is not partitioned
///     by tenant, and its foreign key to <c>mt_streams(id, tenant_id)</c> cascades on DELETE, which a partition
///     detach is not, so <c>ShardedTenancy.RemoveTenantAsync</c> has to delete the tenant's natural key rows first.
///     <see cref="Admin.removing_a_tenant_with_natural_key_rows" /> covers the single-database paths. Both tenants
///     live on the same shard, so they share its natural key table.
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class removing_a_tenant_with_natural_keys_from_its_shard: ShardedPartitionedContext
{
    private const string Removed = "rmnk_alpha";
    private const string Survivor = "rmnk_beta";

    public removing_a_tenant_with_natural_keys_from_its_shard(ShardedPartitionedFixture fixture): base(fixture)
    {
    }

    [Fact]
    public async Task removing_a_tenant_takes_its_natural_keys_and_leaves_another_tenants_alone()
    {
        var store = BuildShardedStore(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Projections.Snapshot<ShardedRemovalCase>(SnapshotLifecycle.Inline);
        });

        // The shard gets the full schema, the natural key table included, before any tenant is registered
        var shard = Fixture.DbNames[0];
        var shardConnStr = Fixture.ConnectionStrings[shard];
        var shardDatabase = await store.Storage.FindOrCreateDatabase(shard);
        await shardDatabase.ApplyAllConfiguredChangesToDatabaseAsync(ct: TestContext.Current.CancellationToken);

        await store.Advanced.AddTenantToShardAsync(Removed, shard, CancellationToken.None);
        await store.Advanced.AddTenantToShardAsync(Survivor, shard, CancellationToken.None);

        await openCaseAsync(store, Removed, "CASE-1");
        var survivorCase = await openCaseAsync(store, Survivor, "CASE-1");

        await store.Advanced.RemoveTenantFromShardAsync(Removed, CancellationToken.None);

        (await countNaturalKeysAsync(shardConnStr, Removed)).ShouldBe(0);
        (await countNaturalKeysAsync(shardConnStr, Survivor))
            .ShouldBe(1, "removing one tenant must leave the other tenant's natural keys alone");

        await using var session = store.LightweightSession(Survivor);
        var found = await session.Events.FetchLatest<ShardedRemovalCase, ShardedRemovalCaseNumber>(
            new ShardedRemovalCaseNumber("CASE-1"), TestContext.Current.CancellationToken);
        found.ShouldNotBeNull();
        found.Id.ShouldBe(survivorCase);
    }

    private static async Task<Guid> openCaseAsync(IDocumentStore store, string tenantId, string number)
    {
        var streamId = Guid.NewGuid();
        await using var session = store.LightweightSession(tenantId);
        session.Events.StartStream<ShardedRemovalCase>(streamId, new ShardedRemovalCaseOpened(streamId, number));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return streamId;
    }

    private static async Task<long> countNaturalKeysAsync(string connectionString, string tenantId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand(
            "select count(*) from public.mt_natural_key_shardedremovalcase where tenant_id = :t");
        cmd.Parameters.AddWithValue("t", tenantId);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}

public sealed record ShardedRemovalCaseNumber(string Value);

public sealed record ShardedRemovalCaseOpened(Guid CaseId, string Number);

public sealed record ShardedRemovalCase
{
    public Guid Id { get; set; }

    [NaturalKey]
    public ShardedRemovalCaseNumber Number { get; set; } = null!;

    [NaturalKeySource]
    public static ShardedRemovalCase Create(ShardedRemovalCaseOpened e) =>
        new() { Id = e.CaseId, Number = new ShardedRemovalCaseNumber(e.Number) };
}
