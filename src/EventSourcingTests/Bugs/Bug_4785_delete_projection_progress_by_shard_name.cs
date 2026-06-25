using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
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

    /// <summary>
    /// Walks every distinct <see cref="ShardName.Identity"/> grammar that the
    /// constructor / <see cref="ShardName.Compose"/> can produce, and confirms
    /// the override deletes the row keyed by that exact Identity. Pins both:
    /// (a) the expected Identity literal for each input combination — a
    /// regression guard if the JasperFx grammar drifts — and (b) the
    /// exact-match WHERE clause in <c>DeleteProjectionProgress</c>.
    /// </summary>
    [Theory]
    [InlineData("Bare",         "All",        1u, null,     "Bare:All")]                  // 2-segment
    [InlineData("WithKey",      "events",     1u, null,     "WithKey:events")]            // 2-segment, custom key
    [InlineData("V2",           "All",        2u, null,     "V2:V2:All")]                 // 3-segment, V-marker
    [InlineData("V7",           "events",     7u, null,     "V7:V7:events")]              // 3-segment, V-marker + custom key
    [InlineData("TenantOnly",   "All",        1u, "alpha",  "TenantOnly:All:alpha")]      // 3-segment, no V-marker
    [InlineData("TenantOnly",   "events",     1u, "bravo",  "TenantOnly:events:bravo")]   // 3-segment, no V-marker + custom key
    [InlineData("V2T",          "All",        3u, "delta",  "V2T:V3:All:delta")]          // 4-segment, version + tenant
    [InlineData("V2T",          "events",     5u, "echo",   "V2T:V5:events:echo")]        // 4-segment, version + tenant + custom key
    public async Task deletes_row_for_every_shard_name_identity_permutation(
        string name, string shardKey, uint version, string? tenantId, string expectedIdentity)
    {
        // Pin the grammar contract first — if JasperFx ever changes Identity
        // composition, this assertion catches it before the delete claim.
        var shard = new ShardName(name, shardKey, version, tenantId);
        shard.Identity.ShouldBe(expectedIdentity);

        // And confirm Compose produces the same Identity (round-trip both
        // construction paths).
        ShardName.Compose(name, shardKey, tenantId, version).Identity.ShouldBe(expectedIdentity);

        var database = (MartenDatabase)theStore.Storage.Database;
        await database.EnsureStorageExistsAsync(typeof(IEvent));

        await seedProgressionRow(shard.Identity, 42);
        (await readSequenceFor(shard.Identity)).ShouldBe(42);

        await ((IEventDatabase)database).DeleteProjectionProgressByShardNameAsync(shard.Identity, default);

        (await readSequenceFor(shard.Identity)).ShouldBeNull();
    }

    [Fact]
    public async Task default_shardkey_constructor_produces_All_and_deletes()
    {
        // The convenience ctor `new ShardName(name)` fills shardKey="All",
        // version=1, tenant=null — distinct entry point from the 4-arg ctor
        // even though Identity collapses into the 2-segment grammar.
        var shard = new ShardName("DefaultCtor");
        shard.Identity.ShouldBe("DefaultCtor:All");

        var database = (MartenDatabase)theStore.Storage.Database;
        await database.EnsureStorageExistsAsync(typeof(IEvent));

        await seedProgressionRow(shard.Identity, 7);
        await ((IEventDatabase)database).DeleteProjectionProgressByShardNameAsync(shard.Identity, default);

        (await readSequenceFor(shard.Identity)).ShouldBeNull();
    }

    [Fact]
    public async Task high_water_mark_identity_is_the_literal_constant_and_is_deletable_by_it()
    {
        // The HighWaterMark name is a special case inside the ShardName ctor —
        // Identity is overwritten to the literal "HighWaterMark" regardless of
        // the shardKey/version/tenant arguments. The override matches on the
        // exact name column, so deleting via this literal works the same as
        // any other identity. (Operational note: HighWaterMark is not an
        // orphan — only delete it deliberately.)
        var shard = new ShardName(ShardState.HighWaterMark, "ignored", 9, "ignored");
        shard.Identity.ShouldBe(ShardState.HighWaterMark);

        var database = (MartenDatabase)theStore.Storage.Database;
        await database.EnsureStorageExistsAsync(typeof(IEvent));

        await seedProgressionRow(ShardState.HighWaterMark, 999);
        await ((IEventDatabase)database).DeleteProjectionProgressByShardNameAsync(
            ShardState.HighWaterMark, default);

        (await readSequenceFor(ShardState.HighWaterMark)).ShouldBeNull();
    }

    [Fact]
    public async Task deleting_one_permutation_leaves_every_other_grammar_intact()
    {
        // Sanity check: seed every distinct Identity grammar at once and
        // delete one — the exact-match WHERE clause must touch ONLY that row
        // and leave the rest of the grammar set untouched.
        var database = (MartenDatabase)theStore.Storage.Database;
        await database.EnsureStorageExistsAsync(typeof(IEvent));

        var identities = new[]
        {
            new ShardName("P", "All", 1, null).Identity,            // P:All
            new ShardName("P", "events", 1, null).Identity,         // P:events
            new ShardName("P", "All", 2, null).Identity,            // P:V2:All
            new ShardName("P", "events", 2, null).Identity,         // P:V2:events
            new ShardName("P", "All", 1, "alpha").Identity,         // P:All:alpha
            new ShardName("P", "events", 1, "bravo").Identity,      // P:events:bravo
            new ShardName("P", "All", 2, "delta").Identity,         // P:V2:All:delta
            new ShardName("P", "events", 2, "echo").Identity,       // P:V2:events:echo
            ShardState.HighWaterMark                                  // HighWaterMark
        };

        for (var i = 0; i < identities.Length; i++)
        {
            await seedProgressionRow(identities[i], 100 + i);
        }

        // Delete the 4-segment versioned+tenant identity (the most specific
        // grammar) and assert every sibling survives.
        var target = new ShardName("P", "events", 2, "echo").Identity;
        await ((IEventDatabase)database).DeleteProjectionProgressByShardNameAsync(target, default);

        for (var i = 0; i < identities.Length; i++)
        {
            var actual = await readSequenceFor(identities[i]);
            if (identities[i] == target)
            {
                actual.ShouldBeNull();
            }
            else
            {
                actual.ShouldBe(100 + i);
            }
        }
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
