#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
///     Every <c>mt_event_tag_*</c> table carries a foreign key to <c>mt_events(seq_id)</c> with Weasel's
///     default action, NO ACTION -- not the <c>ON DELETE CASCADE</c> the storage-mode docs used to show. So
///     anything that deletes a tagged event has to delete its tag rows first, the way
///     <c>TombstoneStream</c> (#5673) and <c>mt_archive_stream</c> (#5675) do. Stream compaction,
///     <c>IDocumentCleaner.DeleteSingleEventStreamAsync</c> and <c>DeleteAllTenantDataAsync</c> did not, and
///     came back as 23503 on any stream whose deleted events carried a tag.
///     <para>
///     The tag tables' primary key leads with <c>value</c>, so neither that foreign key's check nor a delete
///     of tag rows by event could use an index: the second half of this class pins one leading with
///     <c>seq_id</c>.
///     </para>
/// </summary>
public class deleting_tagged_events: OneOffConfigurationsContext
{
    public record LedgerOpened(string Name);

    public record LedgerPosted(decimal Amount);

    public record LedgerId(Guid Value);

    public record AuditorId(string Value);

    private void configure(TenancyStyle tenancy = TenancyStyle.Single, DcbStorageMode mode = DcbStorageMode.TagTables,
        bool twoTagTypes = false, bool archivedPartitioning = false,
        StreamIdentity identity = StreamIdentity.AsGuid)
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = tenancy;
            opts.Events.DcbStorageMode = mode;
            opts.Events.StreamIdentity = identity;
            opts.Events.UseArchivedStreamPartitioning = archivedPartitioning;
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();

            opts.Events.RegisterTagType<LedgerId, Guid>(id => id.Value, "ledger");

            // A second tag table, because the delete has to reach every registered tag type -- one tag type
            // passing proves nothing about the loop.
            if (twoTagTypes) opts.Events.RegisterTagType<AuditorId, string>(id => id.Value, "auditor");
        });
    }

    private static IEvent tagged(IDocumentSession session, object data, params object[] tags)
    {
        var e = session.Events.BuildEvent(data);
        e.WithTag(tags);
        return e;
    }

    [Fact]
    public async Task compacting_a_tagged_stream_deletes_the_tag_rows_of_the_folded_events()
    {
        configure();

        var streamId = Guid.NewGuid();
        var neighbourId = Guid.NewGuid();
        var tag = new LedgerId(Guid.NewGuid());

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<TagCompactionLedger>(streamId,
                tagged(session, new LedgerOpened("main"), tag),
                tagged(session, new LedgerPosted(10), tag),
                tagged(session, new LedgerPosted(5), tag));

            // The same tag value on another stream: its row is keyed by an event compaction does not fold, and
            // must survive
            session.Events.StartStream<TagCompactionLedger>(neighbourId,
                tagged(session, new LedgerOpened("neighbour"), tag));

            await session.SaveChangesAsync();
        }

        (await countTagRows("ledger", tag.Value)).ShouldBe(4);

        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<TagCompactionLedger>(streamId);
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var compacted = (await query.Events.FetchStreamAsync(streamId)).Single()
                .Data.ShouldBeOfType<Compacted<TagCompactionLedger>>();
            compacted.Snapshot.Balance.ShouldBe(15);

            (await query.Events.FetchStreamAsync(neighbourId)).Count.ShouldBe(1);
        }

        // The two folded events took their tag rows with them. The survivor -- now the Compacted<T> event --
        // keeps its own, and so does the neighbour.
        (await countTagRows("ledger", tag.Value)).ShouldBe(2);
        (await countOrphanedTagRows("ledger")).ShouldBe(0);
    }

    [Fact]
    public async Task compacting_deletes_the_rows_of_every_registered_tag_table()
    {
        configure(twoTagTypes: true);

        var streamId = Guid.NewGuid();
        var ledger = new LedgerId(Guid.NewGuid());
        var auditor = new AuditorId("auditor-" + Guid.NewGuid().ToString("N"));

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<TagCompactionLedger>(streamId,
                tagged(session, new LedgerOpened("both"), ledger, auditor),
                tagged(session, new LedgerPosted(1), ledger, auditor),
                tagged(session, new LedgerPosted(2), ledger, auditor));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<TagCompactionLedger>(streamId);
            await session.SaveChangesAsync();
        }

        (await countTagRows("ledger", ledger.Value)).ShouldBe(1);
        (await countTagRows("auditor", auditor.Value)).ShouldBe(1);
    }

    [Fact]
    public async Task compacting_a_tagged_stream_under_conjoined_tenancy_leaves_other_tenants_alone()
    {
        configure(TenancyStyle.Conjoined);

        var streamId = Guid.NewGuid();
        var tag = new LedgerId(Guid.NewGuid());

        // The same stream id and the same tag value in two tenants
        foreach (var tenant in new[] { "one", "two" })
        {
            await using var session = theStore.LightweightSession(tenant);
            session.Events.StartStream<TagCompactionLedger>(streamId,
                tagged(session, new LedgerOpened(tenant), tag),
                tagged(session, new LedgerPosted(10), tag),
                tagged(session, new LedgerPosted(5), tag));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("one"))
        {
            await session.Events.CompactStreamAsync<TagCompactionLedger>(streamId);
            await session.SaveChangesAsync();
        }

        (await countTagRows("ledger", tag.Value, tenantId: "one")).ShouldBe(1);
        (await countTagRows("ledger", tag.Value, tenantId: "two")).ShouldBe(3);

        await using var two = theStore.QuerySession("two");
        (await two.Events.FetchStreamAsync(streamId)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task compacting_a_tagged_stream_under_archived_stream_partitioning()
    {
        // Each tag table is itself partitioned by is_archived here, with a composite (seq_id, is_archived)
        // foreign key; the delete goes through the parent and still has to clear the live partition.
        configure(archivedPartitioning: true);

        var streamId = Guid.NewGuid();
        var tag = new LedgerId(Guid.NewGuid());

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<TagCompactionLedger>(streamId,
                tagged(session, new LedgerOpened("partitioned"), tag),
                tagged(session, new LedgerPosted(10), tag));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<TagCompactionLedger>(streamId);
            await session.SaveChangesAsync();
        }

        (await countTagRows("ledger", tag.Value)).ShouldBe(1);
    }

    [Fact]
    public async Task compacting_a_tagged_string_identified_stream()
    {
        configure(identity: StreamIdentity.AsString);

        var streamKey = "ledger-" + Guid.NewGuid().ToString("N");
        var tag = new LedgerId(Guid.NewGuid());

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<TagCompactionLedgerByKey>(streamKey,
                tagged(session, new LedgerOpened("keyed"), tag),
                tagged(session, new LedgerPosted(10), tag),
                tagged(session, new LedgerPosted(5), tag));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<TagCompactionLedgerByKey>(streamKey);
            await session.SaveChangesAsync();
        }

        (await countTagRows("ledger", tag.Value)).ShouldBe(1);
    }

    [Fact]
    public async Task compacting_in_hstore_mode_still_works()
    {
        // No tag tables at all -- the tags ride on mt_events.tags and go with the row. Nothing may be emitted for
        // a table that does not exist.
        configure(mode: DcbStorageMode.HStore);

        var streamId = Guid.NewGuid();
        var tag = new LedgerId(Guid.NewGuid());

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<TagCompactionLedger>(streamId,
                tagged(session, new LedgerOpened("hstore"), tag),
                tagged(session, new LedgerPosted(10), tag));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<TagCompactionLedger>(streamId);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamAsync(streamId)).Single().Data
            .ShouldBeOfType<Compacted<TagCompactionLedger>>().Snapshot.Balance.ShouldBe(10);
    }

    [Fact]
    public async Task deleting_a_single_tagged_stream_through_the_cleaner()
    {
        configure();

        var doomed = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var tag = new LedgerId(Guid.NewGuid());

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(doomed, tagged(session, new LedgerOpened("doomed"), tag),
                tagged(session, new LedgerPosted(1), tag));
            session.Events.StartStream(kept, tagged(session, new LedgerOpened("kept"), tag));
            await session.SaveChangesAsync();
        }

        await theStore.Advanced.Clean.DeleteSingleEventStreamAsync(doomed);

        (await countTagRows("ledger", tag.Value)).ShouldBe(1);

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamAsync(doomed)).ShouldBeEmpty();
        (await query.Events.FetchStreamAsync(kept)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task deleting_a_single_tagged_stream_of_one_tenant_through_the_cleaner()
    {
        configure(TenancyStyle.Conjoined);

        var streamId = Guid.NewGuid();
        var tag = new LedgerId(Guid.NewGuid());

        foreach (var tenant in new[] { "one", "two" })
        {
            await using var session = theStore.LightweightSession(tenant);
            session.Events.StartStream(streamId, tagged(session, new LedgerOpened(tenant), tag),
                tagged(session, new LedgerPosted(1), tag));
            await session.SaveChangesAsync();
        }

        await theStore.Advanced.Clean.DeleteSingleEventStreamAsync(streamId, "one");

        (await countTagRows("ledger", tag.Value, tenantId: "one")).ShouldBe(0);
        (await countTagRows("ledger", tag.Value, tenantId: "two")).ShouldBe(2);

        await using var two = theStore.QuerySession("two");
        (await two.Events.FetchStreamAsync(streamId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task deleting_all_of_a_tenants_data_takes_its_tag_rows()
    {
        configure(TenancyStyle.Conjoined);

        var streamId = Guid.NewGuid();
        var tag = new LedgerId(Guid.NewGuid());

        foreach (var tenant in new[] { "one", "two" })
        {
            await using var session = theStore.LightweightSession(tenant);
            session.Events.StartStream(streamId, tagged(session, new LedgerOpened(tenant), tag));
            await session.SaveChangesAsync();
        }

        await theStore.Advanced.DeleteAllTenantDataAsync("one", CancellationToken.None);

        (await countTagRows("ledger", tag.Value, tenantId: "one")).ShouldBe(0);
        (await countTagRows("ledger", tag.Value, tenantId: "two")).ShouldBe(1);

        await using var two = theStore.QuerySession("two");
        (await two.Events.FetchStreamAsync(streamId)).Count.ShouldBe(1);
    }

    public static TheoryData<TenancyStyle, bool> TagTableShapes => new()
    {
        { TenancyStyle.Single, false },
        { TenancyStyle.Conjoined, false },
        { TenancyStyle.Single, true },
        { TenancyStyle.Conjoined, true }
    };

    /// <summary>
    ///     The primary key is <c>(value, [tenant_id], seq_id)</c>: right for "which events carry this tag", useless
    ///     for "which tag rows belong to this event". That second question is the one the foreign key's check asks
    ///     for every <c>mt_events</c> row any path deletes, and the one every tag delete above asks of each tag
    ///     table -- without an index leading with <c>seq_id</c> each is a scan of the whole tag table.
    /// </summary>
    [Theory]
    [MemberData(nameof(TagTableShapes))]
    public async Task every_tag_table_has_an_index_leading_with_seq_id(TenancyStyle tenancy, bool archivedPartitioning)
    {
        _schemaName = $"tag_seq_idx_{tenancy}_{(archivedPartitioning ? "part" : "flat")}".ToLowerInvariant();
        configure(tenancy, twoTagTypes: true, archivedPartitioning: archivedPartitioning);

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await indexesLeadingWithSeqId("mt_event_tag_ledger")).ShouldBe(new[] { "idx_mt_event_tag_ledger_seq_id" });
        (await indexesLeadingWithSeqId("mt_event_tag_auditor")).ShouldBe(new[] { "idx_mt_event_tag_auditor_seq_id" });

        // And the declared shape is the one in the database: nothing left over for the next migration
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    /// <summary>
    ///     An existing store has the tag tables without the index. Bringing it forward must be an additive
    ///     <c>CREATE INDEX</c> and nothing else -- no rebuilt table, no dropped key.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task an_existing_tag_table_gains_the_index_by_an_additive_migration(bool archivedPartitioning)
    {
        // Under archived partitioning the tag table is itself partitioned, which is where a migration could
        // otherwise decide to rebuild it
        _schemaName = $"tag_seq_idx_migration_{(archivedPartitioning ? "part" : "flat")}";
        configure(archivedPartitioning: archivedPartitioning);

        await using (var session = theStore.LightweightSession())
        {
            var tag = new LedgerId(Guid.NewGuid());
            session.Events.StartStream(Guid.NewGuid(), tagged(session, new LedgerOpened("existing"), tag));
            await session.SaveChangesAsync();
        }

        // The shape a store created before the index existed
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand($"drop index if exists {SchemaName}.idx_mt_event_tag_ledger_seq_id").ExecuteNonQueryAsync();
        }

        (await indexesLeadingWithSeqId("mt_event_tag_ledger")).ShouldBeEmpty();

        var migration = await theStore.Storage.Database.CreateMigrationAsync();
        migration.Difference.ShouldBe(SchemaPatchDifference.Update);

        var sql = migration.UpdateSql();
        TestContext.Current.TestOutputHelper?.WriteLine(sql);
        sql.ShouldContain("idx_mt_event_tag_ledger_seq_id");
        sql.ShouldNotContain("drop", Case.Insensitive);
        sql.ShouldNotContain("alter table", Case.Insensitive);

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await indexesLeadingWithSeqId("mt_event_tag_ledger")).ShouldBe(new[] { "idx_mt_event_tag_ledger_seq_id" });
        (await countTagRows("ledger", null)).ShouldBe(1);
    }

    /// <summary>
    ///     The documented way round the migration's write lock on a large tag table: build the index out of band
    ///     with <c>CONCURRENTLY</c> under the same name before deploying. The next migration must find nothing to do.
    /// </summary>
    [Fact]
    public async Task an_index_built_out_of_band_is_left_alone_by_the_migration()
    {
        configure();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(Guid.NewGuid(),
                tagged(session, new LedgerOpened("existing"), new LedgerId(Guid.NewGuid())));
            await session.SaveChangesAsync();
        }

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand($"drop index if exists {SchemaName}.idx_mt_event_tag_ledger_seq_id").ExecuteNonQueryAsync();

            // What an operator would run ahead of the deployment
            await conn.CreateCommand(
                    $"create index concurrently idx_mt_event_tag_ledger_seq_id on {SchemaName}.mt_event_tag_ledger (seq_id)")
                .ExecuteNonQueryAsync();
        }

        var migration = await theStore.Storage.Database.CreateMigrationAsync();
        migration.Difference.ShouldBe(SchemaPatchDifference.None);
    }

    private async Task<string[]> indexesLeadingWithSeqId(string table)
    {
        await using var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await connection.OpenAsync();

        // The parent's indexes only: under archived partitioning each partition carries a child of the same index
        await using var command = connection.CreateCommand("""
                                                           select ic.relname
                                                           from pg_index i
                                                           join pg_class ic on ic.oid = i.indexrelid
                                                           join pg_class t on t.oid = i.indrelid
                                                           join pg_namespace n on n.oid = t.relnamespace
                                                           join pg_attribute a on a.attrelid = t.oid and a.attnum = i.indkey[0]
                                                           where n.nspname = :schema and t.relname = :table
                                                             and a.attname = 'seq_id'
                                                           order by ic.relname
                                                           """)
            .With("schema", SchemaName)
            .With("table", table);

        var names = new System.Collections.Generic.List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));

        return names.ToArray();
    }

    private async Task<long> countTagRows(string suffix, object? value, string? tenantId = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await connection.OpenAsync();

        var sql = $"select count(*) from {SchemaName}.mt_event_tag_{suffix} where true";
        if (value != null) sql += " and value = :value";
        if (tenantId != null) sql += " and tenant_id = :tenant";

        await using var command = connection.CreateCommand(sql);
        if (value != null) command.With("value", value);
        if (tenantId != null) command.With("tenant", tenantId);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> countOrphanedTagRows(string suffix)
    {
        await using var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand(
            $"select count(*) from {SchemaName}.mt_event_tag_{suffix} t where not exists (select 1 from {SchemaName}.mt_events e where e.seq_id = t.seq_id)");

        return (long)(await command.ExecuteScalarAsync())!;
    }
}

public class TagCompactionLedger
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Balance { get; set; }

    public void Apply(deleting_tagged_events.LedgerOpened e) => Name = e.Name;
    public void Apply(deleting_tagged_events.LedgerPosted e) => Balance += e.Amount;
}

public class TagCompactionLedgerByKey
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Balance { get; set; }

    public void Apply(deleting_tagged_events.LedgerOpened e) => Name = e.Name;
    public void Apply(deleting_tagged_events.LedgerPosted e) => Balance += e.Amount;
}
