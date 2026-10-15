using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Archiving;

/// <summary>
///     #5675. With <c>UseArchivedStreamPartitioning</c> on, each <c>mt_event_tag_*</c> table is itself
///     partitioned by <c>is_archived</c> and carries a composite foreign key <c>(seq_id, is_archived)</c> to
///     <c>mt_events</c>. <c>mt_archive_stream</c> copied the events into the archived partition and deleted
///     the originals without moving the tag rows, so the delete was refused: archiving any tagged stream
///     came back as a foreign key violation.
/// </summary>
public class Bug_5675_archiving_a_tagged_stream: OneOffConfigurationsContext
{
    public record LedgerOpened(string Name);

    public record LedgerPosted(decimal Amount);

    public record LedgerId(Guid Value);

    public record AuditorId(string Value);

    private void configure(TenancyStyle tenancy = TenancyStyle.Single, DcbStorageMode mode = DcbStorageMode.TagTables,
        bool twoTagTypes = false)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.TenancyStyle = tenancy;
            opts.Events.DcbStorageMode = mode;
            opts.Events.AddEventType<LedgerOpened>();
            opts.Events.AddEventType<LedgerPosted>();

            opts.Events.RegisterTagType<LedgerId, Guid>(id => id.Value, "ledger");

            // A second tag table, because mt_archive_stream has to emit statements for every registered
            // tag type -- one tag type passing proves nothing about the loop.
            if (twoTagTypes) opts.Events.RegisterTagType<AuditorId, string>(id => id.Value, "auditor");
        });
    }

    [Fact]
    public async Task archiving_a_tagged_stream_moves_its_tag_rows()
    {
        configure();

        var archived = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var archivedTag = new LedgerId(Guid.NewGuid());
        var keptTag = new LedgerId(Guid.NewGuid());

        await using (var session = theStore.LightweightSession())
        {
            var opened = session.Events.BuildEvent(new LedgerOpened("archived"));
            opened.WithTag(archivedTag);
            var posted = session.Events.BuildEvent(new LedgerPosted(10));
            posted.WithTag(archivedTag);
            session.Events.StartStream(archived, opened, posted);

            var neighbour = session.Events.BuildEvent(new LedgerOpened("kept"));
            neighbour.WithTag(keptTag);
            session.Events.StartStream(kept, neighbour);

            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.ArchiveStream(archived);
            await session.SaveChangesAsync();
        }

        // The two tag rows moved into the archived partition with their events, and none was left behind
        // in the live one
        (await countTagRows("ledger", archivedTag.Value, isArchived: true)).ShouldBe(2);
        (await countTagRows("ledger", archivedTag.Value, isArchived: false)).ShouldBe(0);

        // The neighbouring stream's tag is untouched
        (await countTagRows("ledger", keptTag.Value, isArchived: false)).ShouldBe(1);
        (await countTagRows("ledger", keptTag.Value, isArchived: true)).ShouldBe(0);
    }

    [Fact]
    public async Task archiving_moves_every_registered_tag_table()
    {
        configure(twoTagTypes: true);

        var streamId = Guid.NewGuid();
        var ledger = new LedgerId(Guid.NewGuid());
        var auditor = new AuditorId("auditor-" + Guid.NewGuid().ToString("N"));

        await using (var session = theStore.LightweightSession())
        {
            var opened = session.Events.BuildEvent(new LedgerOpened("both"));
            opened.WithTag(ledger, auditor);
            session.Events.StartStream(streamId, opened);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.ArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        (await countTagRows("ledger", ledger.Value, isArchived: true)).ShouldBe(1);
        (await countTagRows("ledger", ledger.Value, isArchived: false)).ShouldBe(0);
        (await countTagRows("auditor", auditor.Value, isArchived: true)).ShouldBe(1);
        (await countTagRows("auditor", auditor.Value, isArchived: false)).ShouldBe(0);
    }

    [Fact]
    public async Task archiving_a_tagged_stream_under_conjoined_tenancy_leaves_other_tenants_alone()
    {
        configure(TenancyStyle.Conjoined);

        var streamId = Guid.NewGuid();
        var tag = new LedgerId(Guid.NewGuid());

        // The same stream id and the same tag value in two tenants: the tenant predicate in the archive
        // function has to tell them apart, and the subquery's tenant_id must not be read as the tag
        // table's own.
        foreach (var tenant in new[] { "one", "two" })
        {
            await using var session = theStore.LightweightSession(tenant);
            var opened = session.Events.BuildEvent(new LedgerOpened(tenant));
            opened.WithTag(tag);
            session.Events.StartStream(streamId, opened);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("one"))
        {
            session.Events.ArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        (await countTagRows("ledger", tag.Value, isArchived: true, tenantId: "one")).ShouldBe(1);
        (await countTagRows("ledger", tag.Value, isArchived: false, tenantId: "one")).ShouldBe(0);

        (await countTagRows("ledger", tag.Value, isArchived: false, tenantId: "two")).ShouldBe(1);
        (await countTagRows("ledger", tag.Value, isArchived: true, tenantId: "two")).ShouldBe(0);
    }

    [Fact]
    public async Task an_untagged_stream_beside_a_registered_tag_type_still_archives()
    {
        // The tag statements run whether or not this particular stream has tag rows; nothing may assume
        // there is at least one.
        configure();

        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new LedgerOpened("untagged"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.ArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        (await countEventRows(streamId, isArchived: true)).ShouldBe(1);
        (await countEventRows(streamId, isArchived: false)).ShouldBe(0);
    }

    [Fact]
    public async Task archiving_a_tagged_stream_in_hstore_mode_still_works()
    {
        // HStore mode has no tag tables at all -- the tags ride along on mt_events.tags, and so move with
        // the row. The archive function must emit no tag statements for a table that does not exist.
        configure(mode: DcbStorageMode.HStore);

        var streamId = Guid.NewGuid();
        var tag = new LedgerId(Guid.NewGuid());

        await using (var session = theStore.LightweightSession())
        {
            var opened = session.Events.BuildEvent(new LedgerOpened("hstore"));
            opened.WithTag(tag);
            session.Events.StartStream(streamId, opened);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.ArchiveStream(streamId);
            await session.SaveChangesAsync();
        }

        (await countEventRows(streamId, isArchived: true)).ShouldBe(1);
        (await countEventRows(streamId, isArchived: false)).ShouldBe(0);
    }

    [Fact]
    public async Task a_tag_query_still_sees_the_archived_events_which_is_a_separate_bug()
    {
        // Pinning what it DOES, not what it should. #5675 asked that tag queries "behave as documented"
        // after archiving, and they do not behave like any other event query: QueryAllRawEvents() excludes
        // archived events by default, and QueryByTagsAsync / EventsExistAsync do not. Verified independent
        // of this fix -- the same two counts come back with UseArchivedStreamPartitioning OFF, which the
        // archive function change here never touches -- so it is pre-existing and filed as #5677. Change
        // this test when that one is fixed; it is here so the current answer is written down rather than
        // discovered again.
        configure();

        var archived = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var tag = new LedgerId(Guid.NewGuid());

        await using (var session = theStore.LightweightSession())
        {
            var first = session.Events.BuildEvent(new LedgerOpened("archived"));
            first.WithTag(tag);
            session.Events.StartStream(archived, first);

            var second = session.Events.BuildEvent(new LedgerOpened("kept"));
            second.WithTag(tag);
            session.Events.StartStream(kept, second);

            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.ArchiveStream(archived);
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.LightweightSession())
        {
            // The archived row moved partitions and is still reachable by tag -- which is the point worth
            // pinning here: the move does not LOSE the tag rows either
            (await query.Events.QueryByTagsAsync(new EventTagQuery().Or<LedgerId>(tag))).Count.ShouldBe(2);

            // While the ordinary event query does exclude it
            (await query.Events.QueryAllRawEvents().ToListAsync()).Count.ShouldBe(1);
        }
    }

    private async Task<long> countTagRows(string suffix, object value, bool isArchived, string? tenantId = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await connection.OpenAsync();

        var sql =
            $"select count(*) from {SchemaName}.mt_event_tag_{suffix} where value = :value and is_archived = :archived";
        if (tenantId != null) sql += " and tenant_id = :tenant";

        await using var command = connection.CreateCommand(sql)
            .With("value", value)
            .With("archived", isArchived);
        if (tenantId != null) command.With("tenant", tenantId);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> countEventRows(Guid streamId, bool isArchived)
    {
        await using var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection
            .CreateCommand(
                $"select count(*) from {SchemaName}.mt_events where stream_id = :stream and is_archived = :archived")
            .With("stream", streamId)
            .With("archived", isArchived);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
