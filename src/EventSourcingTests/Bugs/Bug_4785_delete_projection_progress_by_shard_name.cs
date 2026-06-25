using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using NpgsqlTypes;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #4785 / jasperfx#473 — <see cref="MartenDatabase"/> override of
/// <c>IEventDatabase.DeleteProjectionProgressByShardNameAsync</c>. The default
/// JasperFx.Events implementation throws <see cref="System.NotSupportedException"/>;
/// the override deletes the single <c>mt_event_progression</c> row whose
/// <c>name</c> equals the raw <see cref="ShardName.Identity"/> verbatim,
/// independent of whether a matching projection is still registered. This is
/// the eject path for an orphan/renamed/removed shard that the registered-projection
/// keyed <c>IEventStore&lt;,&gt;.DeleteProjectionProgressAsync</c> cannot target
/// (that path throws <see cref="System.ArgumentOutOfRangeException"/> on unknown
/// names).
/// </summary>
public class Bug_4785_delete_projection_progress_by_shard_name: BugIntegrationContext
{
    [Fact]
    public async Task deletes_an_orphan_progression_row_by_raw_shard_identity()
    {
        var database = (MartenDatabase)theStore.Storage.Database;
        await database.EnsureStorageExistsAsync(typeof(IEvent));

        // A shard identity that intentionally does NOT match any registered
        // projection — the orphan/eject scenario the API is for.
        const string orphanIdentity = "RetiredProjection:V3:All";

        await seedProgressionRow(orphanIdentity, 12345);
        (await readSequenceFor(orphanIdentity)).ShouldBe(12345);

        await ((IEventDatabase)database).DeleteProjectionProgressByShardNameAsync(orphanIdentity, default);

        (await readSequenceFor(orphanIdentity)).ShouldBeNull();
    }

    [Fact]
    public async Task non_existent_identity_is_a_clean_no_op()
    {
        var database = (MartenDatabase)theStore.Storage.Database;
        await database.EnsureStorageExistsAsync(typeof(IEvent));

        // No row exists for this identity — must NOT throw (the prior
        // registered-projection-keyed path threw ArgumentOutOfRangeException
        // in exactly this case).
        await ((IEventDatabase)database).DeleteProjectionProgressByShardNameAsync(
            "NeverWrittenProjection:V1:All", default);

        // The well-known high-water row still exists; nothing collateral was touched.
        (await readSequenceFor("RetiredProjection:V3:All")).ShouldBeNull();
    }

    [Fact]
    public async Task only_the_matching_identity_is_deleted()
    {
        var database = (MartenDatabase)theStore.Storage.Database;
        await database.EnsureStorageExistsAsync(typeof(IEvent));

        await seedProgressionRow("Alpha:V1:All", 1);
        await seedProgressionRow("Beta:V1:All", 2);
        await seedProgressionRow("Gamma:V1:All", 3);

        await ((IEventDatabase)database).DeleteProjectionProgressByShardNameAsync("Beta:V1:All", default);

        (await readSequenceFor("Alpha:V1:All")).ShouldBe(1);
        (await readSequenceFor("Beta:V1:All")).ShouldBeNull();
        (await readSequenceFor("Gamma:V1:All")).ShouldBe(3);
    }

    [Fact]
    public async Task per_tenant_identity_deletes_only_that_tenants_row()
    {
        // The :tenantId suffix on a ShardName.Identity (the
        // {Name}:{ShardKey}:{tenantId} 3-segment grammar) is part of the
        // exact-match key — so per-tenant scoping flows through the identity.
        var database = (MartenDatabase)theStore.Storage.Database;
        await database.EnsureStorageExistsAsync(typeof(IEvent));

        await seedProgressionRow("PerTenantProj:All:alpha", 10);
        await seedProgressionRow("PerTenantProj:All:bravo", 20);

        await ((IEventDatabase)database).DeleteProjectionProgressByShardNameAsync(
            "PerTenantProj:All:alpha", default);

        (await readSequenceFor("PerTenantProj:All:alpha")).ShouldBeNull();
        (await readSequenceFor("PerTenantProj:All:bravo")).ShouldBe(20);
    }

    private async Task seedProgressionRow(string name, long seq)
    {
        var table = theStore.Events.ProgressionTable;
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        try
        {
            await conn.CreateCommand(
                    $"insert into {table} (name, last_seq_id) values (:name, :seq) on conflict (name) do update set last_seq_id = excluded.last_seq_id")
                .With("name", name, NpgsqlDbType.Varchar)
                .With("seq", seq, NpgsqlDbType.Bigint)
                .ExecuteNonQueryAsync();
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private async Task<long?> readSequenceFor(string name)
    {
        var table = theStore.Events.ProgressionTable;
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        try
        {
            var raw = await conn.CreateCommand(
                    $"select last_seq_id from {table} where name = :name")
                .With("name", name, NpgsqlDbType.Varchar)
                .ExecuteScalarAsync();
            return raw is null or System.DBNull ? null : (long?)raw;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
