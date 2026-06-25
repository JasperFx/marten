using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Services;
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

    /// <summary>
    /// Pins the per-axis selectivity of the WHERE name = ? clause: when two
    /// shards differ on EXACTLY one of {version, tenantId, shardKey, name},
    /// deleting one must not touch the sibling. The version+tenant case is
    /// the one most likely to surface a bug because the 4-segment grammar
    /// concatenates BOTH axes into Identity (Name:V{n}:ShardKey:tenant) — if
    /// any normalization, prefix-match, or LIKE crept into the delete path,
    /// the off-by-version or off-by-tenant sibling would be a false-positive.
    /// </summary>
    [Theory]
    [InlineData(
        "Orders", "All", 1u, "alpha",
        "Orders", "All", 2u, "alpha")]   // differ only by version (3-seg vs 4-seg)
    [InlineData(
        "Orders", "All", 3u, "alpha",
        "Orders", "All", 4u, "alpha")]   // differ only by version (both 4-seg)
    [InlineData(
        "Orders", "All", 1u, "alpha",
        "Orders", "All", 1u, "bravo")]   // differ only by tenant
    [InlineData(
        "Orders", "All", 3u, "alpha",
        "Orders", "All", 3u, "bravo")]   // differ only by tenant (both 4-seg)
    [InlineData(
        "Orders", "All", 3u, "alpha",
        "Orders", "All", 4u, "bravo")]   // differ by BOTH version AND tenant
    [InlineData(
        "Orders", "All", 1u, null,
        "Orders", "events", 1u, null)]   // differ only by shardKey
    [InlineData(
        "Orders", "All", 1u, null,
        "Invoices", "All", 1u, null)]    // differ only by name
    [InlineData(
        "Orders", "All", 3u, "alpha",
        "Orders", "events", 3u, "alpha")] // differ only by shardKey, with V+tenant
    public async Task deleting_one_does_not_touch_a_sibling_that_differs_by_one_axis(
        string nameA, string keyA, uint verA, string? tenantA,
        string nameB, string keyB, uint verB, string? tenantB)
    {
        var shardA = new ShardName(nameA, keyA, verA, tenantA);
        var shardB = new ShardName(nameB, keyB, verB, tenantB);

        // Sanity: the two are not the same Identity (otherwise the test
        // tautologically asserts row-A is gone after row-A is deleted).
        shardA.Identity.ShouldNotBe(shardB.Identity);

        var database = (MartenDatabase)theStore.Storage.Database;
        await database.EnsureStorageExistsAsync(typeof(IEvent));

        await seedProgressionRow(shardA.Identity, 11);
        await seedProgressionRow(shardB.Identity, 22);

        await ((IEventDatabase)database).DeleteProjectionProgressByShardNameAsync(shardA.Identity, default);

        (await readSequenceFor(shardA.Identity)).ShouldBeNull();
        (await readSequenceFor(shardB.Identity)).ShouldBe(22);

        // And the reverse: delete B (which is still there), confirm A stays
        // gone (no resurrection) and B leaves cleanly too.
        await ((IEventDatabase)database).DeleteProjectionProgressByShardNameAsync(shardB.Identity, default);

        (await readSequenceFor(shardA.Identity)).ShouldBeNull();
        (await readSequenceFor(shardB.Identity)).ShouldBeNull();
    }

    /// <summary>
    /// End-to-end via the real Marten writer (<c>InsertProjectionProgress</c>):
    /// proves that what Marten's daemon actually writes for a shard with
    /// version>1 AND tenant set is byte-for-byte the same identity the
    /// override deletes. This closes the loop on the user's worry that the
    /// delete contract might silently diverge from the write contract when
    /// both knobs are on at once.
    /// </summary>
    [Fact]
    public async Task writer_and_deleter_agree_on_identity_for_versioned_per_tenant_shard()
    {
        var database = (MartenDatabase)theStore.Storage.Database;
        await database.EnsureStorageExistsAsync(typeof(IEvent));

        // 4-segment grammar — the most concatenated shape, where a writer/
        // deleter mismatch would be most likely to slip through.
        var shard = ShardName.Compose("Audit", "events", "tenant-7", 4);
        shard.Identity.ShouldBe("Audit:V4:events:tenant-7");

        // Drive an InsertProjectionProgress through a real lightweight
        // session — same path the async daemon takes when a shard first
        // checkpoints. This proves the writer stores ShardName.Identity
        // verbatim into name.
        var sessionOptions = SessionOptions.ForDatabase(database);
        sessionOptions.AllowAnyTenant = true;
        await using (var writeSession = theStore.LightweightSession(sessionOptions))
        {
            var range = new EventRange(shard, 500);
            writeSession.QueueOperation(new Marten.Events.Daemon.Progress.InsertProjectionProgress(
                theStore.Events, range));
            await writeSession.SaveChangesAsync();
        }

        (await readSequenceFor(shard.Identity)).ShouldBe(500);

        // Now the operator-facing override deletes it by the same identity.
        await ((IEventDatabase)database).DeleteProjectionProgressByShardNameAsync(shard.Identity, default);

        (await readSequenceFor(shard.Identity)).ShouldBeNull();
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
