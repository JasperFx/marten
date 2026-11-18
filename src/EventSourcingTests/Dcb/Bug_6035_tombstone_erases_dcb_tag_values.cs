#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

public record Bug6035OtherId(Guid Value);
public record Bug6035Number(int Value);
public record Bug6035Account(long Value);

public class Bug6035Ledger
{
    public Guid Id { get; set; }
    public int Count { get; set; }
    public void Apply(Bug6035Happened _) => Count++;
    public void Apply(Bug6035Noted _) => Count++;
}

/// <summary>
///     #6035: a tag can be personal data, and <c>mt_dcb_tag_version</c> holds it as text, so a tombstone
///     takes the row once no event carries the tag. Doing that has to lose neither a conflict nor another
///     stream's version. Every version now comes from a sequence, which is what makes the delete safe. The
///     same facts as Polecat's <c>tombstone_tag_erasure_tests</c> (polecat#890), for both storage modes.
/// </summary>
public class Bug_6035_tombstone_erases_dcb_tag_values: OneOffConfigurationsContext
{
    private void configure(DcbStorageMode mode, Action<StoreOptions>? more = null)
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<Bug6035Happened>();
            opts.Events.AddEventType<Bug6035Noted>();
            opts.Events.RegisterTagType<Bug6035TagId>("erase_course");
            opts.Events.RegisterTagType<Bug6035OtherId>("erase_student");
            opts.Events.RegisterTagType<Bug6035OtherTag>("erase_email");
            opts.Events.RegisterTagType<Bug6035Number>("erase_number");
            opts.Events.RegisterTagType<Bug6035Account>("erase_account");
            opts.Events.DcbStorageMode = mode;
            more?.Invoke(opts);
        });
    }

    private static IEvent tagged(IDocumentSession session, params object[] tags)
    {
        var e = session.Events.BuildEvent(new Bug6035Happened("one"));
        e.WithTag(tags);
        return e;
    }

    private async Task<Guid> appendAsync(params object[] tags)
    {
        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.Append(streamId, tagged(session, tags));
        await session.SaveChangesAsync();
        return streamId;
    }

    private async Task tombstoneAsync(Guid streamId)
    {
        await using var session = theStore.LightweightSession();
        session.Events.TombstoneStream(streamId);
        await session.SaveChangesAsync();
    }

    private async Task<long> scalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task executeAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    // The tag type's own rows, leaving out the per-event-type versions
    private Task<long> versionRowsAsync(object? tagValue = null)
        => scalarAsync($"select count(*) from {SchemaName}.mt_dcb_tag_version where position(':' in tag_table) = 0"
                       + (tagValue == null ? "" : $" and tag_value = '{tagValue}'"));

    // Every row holding the value, the per-event-type versions included
    private Task<long> allRowsAsync(object tagValue)
        => scalarAsync($"select count(*) from {SchemaName}.mt_dcb_tag_version where tag_value = '{tagValue}'");

    private Task deleteVersionRowsAsync() => executeAsync($"delete from {SchemaName}.mt_dcb_tag_version");

    private Task<long> versionOfAsync(object tagValue)
        => scalarAsync(
            $"select version from {SchemaName}.mt_dcb_tag_version where position(':' in tag_table) = 0 and tag_value = '{tagValue}'");

    /// <summary>
    ///     The sequence in polecat#890, which lost the conflict when the row was simply deleted: the counter
    ///     came back at 1 and C's <c>where version = 1</c> matched a row it had never seen.
    /// </summary>
    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task the_stale_capture_cannot_match_the_row_created_after_the_tombstone(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var query = new EventTagQuery().Or(course);
        var stream = await appendAsync(course);

        // Boundary C reads the tag
        await using var c = theStore.LightweightSession();
        var boundaryC = await c.Events.FetchForWritingByTags<Bug6035Boundary>(query);
        boundaryC.Aggregate!.Count.ShouldBe(1);

        await tombstoneAsync(stream);
        (await allRowsAsync(course.Value)).ShouldBe(0);

        // Boundary E reads nothing, and creates the row again
        await using (var e = theStore.LightweightSession())
        {
            var boundaryE = await e.Events.FetchForWritingByTags<Bug6035Boundary>(query);
            boundaryE.Aggregate.ShouldBeNull();
            boundaryE.AppendOne(tagged(e, course));
            await e.SaveChangesAsync();
        }

        (await versionRowsAsync(course.Value)).ShouldBe(1);

        boundaryC.AppendOne(tagged(c, course));
        await Should.ThrowAsync<DcbConcurrencyException>(() => c.SaveChangesAsync());
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task the_row_goes_with_the_last_stream_carrying_the_tag(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var first = await appendAsync(course);
        var second = await appendAsync(course);

        await tombstoneAsync(first);
        (await versionRowsAsync(course.Value)).ShouldBe(1);

        await tombstoneAsync(second);
        (await allRowsAsync(course.Value)).ShouldBe(0);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task only_the_tags_nothing_else_carries_go(DcbStorageMode mode)
    {
        configure(mode);
        var shared = new Bug6035TagId(Guid.NewGuid());
        var own = new Bug6035OtherId(Guid.NewGuid());
        var doomed = await appendAsync(shared, own);
        await appendAsync(shared);

        await tombstoneAsync(doomed);

        (await allRowsAsync(own.Value)).ShouldBe(0);
        (await versionRowsAsync(shared.Value)).ShouldBe(1);
    }

    /// <summary>The row is keyed by tag type, so the same value under another tag type is another row.</summary>
    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task the_same_value_under_another_tag_type_is_another_row(DcbStorageMode mode)
    {
        configure(mode);
        var value = Guid.NewGuid();
        var doomed = await appendAsync(new Bug6035TagId(value));
        await appendAsync(new Bug6035OtherId(value));

        await tombstoneAsync(doomed);

        (await scalarAsync(
                $"select count(*) from {SchemaName}.mt_dcb_tag_version where tag_table like 'erase_course%' and tag_value = '{value}'"))
            .ShouldBe(0);
        (await scalarAsync(
                $"select count(*) from {SchemaName}.mt_dcb_tag_version where tag_table = 'erase_student' and tag_value = '{value}'"))
            .ShouldBe(1);
    }

    /// <summary>
    ///     #5887: the per-event-type versions hold the value too, keyed by event type, so they go by the
    ///     same rule: once no other stream has an event of that type carrying the tag.
    /// </summary>
    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_per_event_type_version_goes_with_the_last_event_of_its_type(DcbStorageMode mode)
    {
        configure(mode, opts => opts.Events.DcbConflictScope = DcbConflictScope.TagValueAndEventType);
        var course = new Bug6035TagId(Guid.NewGuid());
        var doomed = await appendAsync(course);

        // Another stream carries the tag, on an event of another type
        await using (var session = theStore.LightweightSession())
        {
            var noted = session.Events.BuildEvent(new Bug6035Noted("other"));
            noted.WithTag(course);
            session.Events.Append(Guid.NewGuid(), noted);
            await session.SaveChangesAsync();
        }

        var happened = theStore.Options.EventGraph.EventMappingFor<Bug6035Happened>().EventTypeName;
        var notedType = theStore.Options.EventGraph.EventMappingFor<Bug6035Noted>().EventTypeName;

        await tombstoneAsync(doomed);

        (await versionRowsAsync(course.Value)).ShouldBe(1);
        (await scalarAsync(
                $"select count(*) from {SchemaName}.mt_dcb_tag_version where tag_table = 'erase_course:{happened}' and tag_value = '{course.Value}'"))
            .ShouldBe(0);
        (await scalarAsync(
                $"select count(*) from {SchemaName}.mt_dcb_tag_version where tag_table = 'erase_course:{notedType}' and tag_value = '{course.Value}'"))
            .ShouldBe(1);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_boundary_over_a_tag_another_stream_still_carries_loses_and_the_retry_saves(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var query = new EventTagQuery().Or(course);
        var doomed = await appendAsync(course);
        await appendAsync(course);

        await using (var session = theStore.LightweightSession())
        {
            var boundary = await session.Events.FetchForWritingByTags<Bug6035Boundary>(query);
            boundary.Aggregate!.Count.ShouldBe(2);

            await tombstoneAsync(doomed);

            boundary.AppendOne(tagged(session, course));
            await Should.ThrowAsync<DcbConcurrencyException>(() => session.SaveChangesAsync());
        }

        await using var retry = theStore.LightweightSession();
        var again = await retry.Events.FetchForWritingByTags<Bug6035Boundary>(query);
        again.Aggregate!.Count.ShouldBe(1);
        again.AppendOne(tagged(retry, course));
        await retry.SaveChangesAsync();
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_tombstone_does_not_create_a_row(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var doomed = await appendAsync(course);
        await deleteVersionRowsAsync();

        await tombstoneAsync(doomed);

        (await allRowsAsync(course.Value)).ShouldBe(0);
    }

    /// <summary>
    ///     With no row there is nothing for a tombstone to move or delete, and a capture of 0 would simply
    ///     create one at save, so nothing but the events themselves can say the read went stale.
    /// </summary>
    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_boundary_that_captured_0_over_events_loses_when_they_are_tombstoned(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var query = new EventTagQuery().Or(course);
        var doomed = await appendAsync(course);
        await deleteVersionRowsAsync();

        await using (var session = theStore.LightweightSession())
        {
            var boundary = await session.Events.FetchForWritingByTags<Bug6035Boundary>(query);
            boundary.Aggregate!.Count.ShouldBe(1);

            await tombstoneAsync(doomed);

            boundary.AppendOne(tagged(session, course));
            await Should.ThrowAsync<DcbConcurrencyException>(() => session.SaveChangesAsync());
        }

        // The failed save rolled back, so the value is not in the table by way of that save either
        (await allRowsAsync(course.Value)).ShouldBe(0);

        await using var retry = theStore.LightweightSession();
        var again = await retry.Events.FetchForWritingByTags<Bug6035Boundary>(query);
        again.Aggregate.ShouldBeNull();
        again.AppendOne(tagged(retry, course));
        await retry.SaveChangesAsync();
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_boundary_that_captured_0_loses_when_another_stream_keeps_the_tag(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var doomed = await appendAsync(course);
        await appendAsync(course);
        await deleteVersionRowsAsync();

        await using var session = theStore.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(course));
        boundary.Aggregate!.Count.ShouldBe(2);

        await tombstoneAsync(doomed);
        (await allRowsAsync(course.Value)).ShouldBe(0);

        boundary.AppendOne(tagged(session, course));
        await Should.ThrowAsync<DcbConcurrencyException>(() => session.SaveChangesAsync());
    }

    /// <summary>
    ///     A tag assigned after the fact by a store older than #6078, which left the tag with events and no row.
    ///     Since #6078 the assignment creates the row (<c>Bug_6078_operations_move_dcb_tag_versions</c>), so the
    ///     rows it would have left out are deleted here.
    /// </summary>
    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_tag_assigned_after_the_fact_has_no_row_and_still_conflicts(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var doomed = await appendAsync(new Bug6035TagId(Guid.NewGuid()));

        await using (var assign = theStore.LightweightSession())
        {
            assign.Events.AssignTagWhere(x => x.StreamId == doomed, course);
            await assign.SaveChangesAsync();
        }

        (await allRowsAsync(course.Value)).ShouldBeGreaterThan(0);
        await executeAsync($"delete from {SchemaName}.mt_dcb_tag_version where tag_value = '{course.Value}'");
        (await allRowsAsync(course.Value)).ShouldBe(0);

        await using var session = theStore.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(course));
        boundary.Aggregate!.Count.ShouldBe(1);

        await tombstoneAsync(doomed);

        boundary.AppendOne(tagged(session, course));
        await Should.ThrowAsync<DcbConcurrencyException>(() => session.SaveChangesAsync());
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task the_same_goes_for_a_boundary_fetched_in_a_batch(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var doomed = await appendAsync(course);
        await deleteVersionRowsAsync();

        await using var session = theStore.LightweightSession();
        var batch = session.CreateBatchQuery();
        var fetched = batch.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(course));
        await batch.Execute();
        var boundary = await fetched;
        boundary.Aggregate!.Count.ShouldBe(1);

        await tombstoneAsync(doomed);

        boundary.AppendOne(tagged(session, course));
        await Should.ThrowAsync<DcbConcurrencyException>(() => session.SaveChangesAsync());
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_boundary_with_no_row_is_left_alone_while_its_events_are_all_there(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var other = await appendAsync(new Bug6035TagId(Guid.NewGuid()));
        await appendAsync(course);
        await deleteVersionRowsAsync();

        await using var session = theStore.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(course));

        await tombstoneAsync(other);

        boundary.AppendOne(tagged(session, course));
        await session.SaveChangesAsync();
    }

    /// <summary>One tag with a row and history, one brand new: the mixture the recount runs for.</summary>
    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_boundary_over_a_new_tag_and_an_old_one_saves(DcbStorageMode mode)
    {
        configure(mode);
        var known = new Bug6035TagId(Guid.NewGuid());
        var fresh = new Bug6035TagId(Guid.NewGuid());
        await appendAsync(known);

        await using var session = theStore.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<Bug6035Boundary>(
            new EventTagQuery().Or(known).Or(fresh));
        boundary.Aggregate!.Count.ShouldBe(1);

        boundary.AppendOne(tagged(session, fresh));
        await session.SaveChangesAsync();
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_boundary_over_a_new_tag_and_a_tombstoned_one_loses(DcbStorageMode mode)
    {
        configure(mode);
        var known = new Bug6035TagId(Guid.NewGuid());
        var fresh = new Bug6035TagId(Guid.NewGuid());
        var doomed = await appendAsync(known);

        await using var session = theStore.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<Bug6035Boundary>(
            new EventTagQuery().Or(known).Or(fresh));

        await tombstoneAsync(doomed);

        boundary.AppendOne(tagged(session, fresh));

        // Once rather than twice: the row and the recount agree about one boundary
        await Should.ThrowAsync<DcbConcurrencyException>(() => session.SaveChangesAsync());
    }

    /// <summary>
    ///     The same row read twice, with a tombstone between. Asserting the lower of the two captures used
    ///     to cover every case, until a row could disappear: the lower one is now the "no row" of the newer
    ///     read, and that would have created the row and won.
    /// </summary>
    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task two_boundaries_in_one_session_either_side_of_a_tombstone_lose(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var query = new EventTagQuery().Or(course);
        var doomed = await appendAsync(course);

        await using var session = theStore.LightweightSession();
        var older = await session.Events.FetchForWritingByTags<Bug6035Boundary>(query);
        older.Aggregate!.Count.ShouldBe(1);

        await tombstoneAsync(doomed);

        var newer = await session.Events.FetchForWritingByTags<Bug6035Boundary>(query);
        newer.Aggregate.ShouldBeNull();

        newer.AppendOne(tagged(session, course));
        await Should.ThrowAsync<DcbConcurrencyException>(() => session.SaveChangesAsync());
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task an_archived_stream_still_carries_the_tag(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var archived = await appendAsync(course);
        var doomed = await appendAsync(course);

        await using (var session = theStore.LightweightSession())
        {
            session.Events.ArchiveStream(archived);
            await session.SaveChangesAsync();
        }

        await tombstoneAsync(doomed);

        // Its events are still stored, and un-archiving would bring the value straight back
        (await versionRowsAsync(course.Value)).ShouldBe(1);

        await tombstoneAsync(archived);
        (await allRowsAsync(course.Value)).ShouldBe(0);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task an_append_in_the_same_save_keeps_the_tag(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var doomed = await appendAsync(course);

        await using var session = theStore.LightweightSession();
        session.Events.TombstoneStream(doomed);
        session.Events.Append(Guid.NewGuid(), tagged(session, course));
        await session.SaveChangesAsync();

        (await versionRowsAsync(course.Value)).ShouldBe(1);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task two_tombstones_in_one_save_erase_the_tag_they_share(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var first = await appendAsync(course);
        var second = await appendAsync(course);

        await using var session = theStore.LightweightSession();
        session.Events.TombstoneStream(first);
        session.Events.TombstoneStream(second);
        await session.SaveChangesAsync();

        (await allRowsAsync(course.Value)).ShouldBe(0);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task every_kind_of_tag_value_reaches_its_row(DcbStorageMode mode)
    {
        configure(mode);
        var email = new Bug6035OtherTag("ann@example.com-" + Guid.NewGuid().ToString("N"));
        var number = new Bug6035Number(42);
        var account = new Bug6035Account(9_000_000_000);
        var course = new Bug6035TagId(Guid.NewGuid());
        var doomed = await appendAsync(email, number, account, course);
        (await versionRowsAsync()).ShouldBe(4);

        await tombstoneAsync(doomed);

        (await scalarAsync($"select count(*) from {SchemaName}.mt_dcb_tag_version")).ShouldBe(0);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_string_stream_is_erased_the_same_way(DcbStorageMode mode)
    {
        configure(mode, opts => opts.Events.StreamIdentity = StreamIdentity.AsString);
        // Two tag types, as HStore holds one value per tag type on an event
        var email = new Bug6035OtherTag("ann@example.com");
        var shared = new Bug6035TagId(Guid.NewGuid());

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append("doomed", tagged(session, email, shared));
            session.Events.Append("other", tagged(session, shared));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.TombstoneStream("doomed");
            await session.SaveChangesAsync();
        }

        (await allRowsAsync(email.Value)).ShouldBe(0);
        (await versionRowsAsync(shared.Value)).ShouldBe(1);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables, true)]
    [InlineData(DcbStorageMode.TagTables, false)]
    [InlineData(DcbStorageMode.HStore, true)]
    [InlineData(DcbStorageMode.HStore, false)]
    public async Task erased_with_or_without_archived_partitioning(DcbStorageMode mode, bool archivedPartitioning)
    {
        configure(mode, opts => opts.Events.UseArchivedStreamPartitioning = archivedPartitioning);
        var course = new Bug6035TagId(Guid.NewGuid());
        var shared = new Bug6035OtherId(Guid.NewGuid());
        var doomed = await appendAsync(course, shared);
        await appendAsync(shared);

        await tombstoneAsync(doomed);

        (await allRowsAsync(course.Value)).ShouldBe(0);
        (await versionRowsAsync(shared.Value)).ShouldBe(1);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_tenant_only_loses_its_own_row(DcbStorageMode mode)
    {
        configure(mode, opts => opts.Events.TenancyStyle = TenancyStyle.Conjoined);
        var course = new Bug6035TagId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        foreach (var tenant in new[] { "red", "blue" })
        {
            await using var session = theStore.LightweightSession(tenant);
            session.Events.Append(streamId, tagged(session, course));
            await session.SaveChangesAsync();
        }

        await using (var red = theStore.LightweightSession("red"))
        {
            red.Events.TombstoneStream(streamId);
            await red.SaveChangesAsync();
        }

        (await scalarAsync(
                $"select count(*) from {SchemaName}.mt_dcb_tag_version where tag_value = '{course.Value}' and tenant_id = 'red'"))
            .ShouldBe(0);
        (await scalarAsync(
                $"select count(*) from {SchemaName}.mt_dcb_tag_version where position(':' in tag_table) = 0 and tag_value = '{course.Value}' and tenant_id = 'blue'"))
            .ShouldBe(1);
    }

    /// <summary>
    ///     What a tombstone can't do, pinned because the docs say so: it finds tags through the stream's
    ///     events, and compaction has already deleted them.
    /// </summary>
    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_tag_only_compacted_events_carried_keeps_its_row(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var stream = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Bug6035Ledger>(stream, tagged(session, course));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(stream, new Bug6035Noted("untagged"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<Bug6035Ledger>(stream);
            await session.SaveChangesAsync();
        }

        await tombstoneAsync(stream);

        (await versionRowsAsync(course.Value)).ShouldBe(1);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_value_is_never_given_out_twice(DcbStorageMode mode)
    {
        configure(mode);
        var course = new Bug6035TagId(Guid.NewGuid());
        var seen = new HashSet<long>();

        for (var i = 0; i < 3; i++)
        {
            var first = await appendAsync(course);
            seen.Add(await versionOfAsync(course.Value)).ShouldBeTrue();

            var second = await appendAsync(course);
            seen.Add(await versionOfAsync(course.Value)).ShouldBeTrue();

            await tombstoneAsync(first);
            await tombstoneAsync(second);
        }

        seen.ShouldAllBe(x => x >= DcbTagVersionTable.SequenceStart);
    }
}
