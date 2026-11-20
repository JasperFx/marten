#nullable enable
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
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
///     #6078: archiving, un-archiving, <c>AssignTagWhere</c> and <c>DeleteSingleEventStreamAsync</c> each change
///     which events a DCB boundary reads without appending anything, so each has to move the versions of the
///     tags it touches the way a tombstone does since #6035. Every case is a boundary that reads, the operation,
///     and a save that has to fail.
/// </summary>
public class Bug_6078_operations_move_dcb_tag_versions: OneOffConfigurationsContext
{
    private void configure(DcbStorageMode mode, DcbConflictScope scope = DcbConflictScope.TagValue,
        TenancyStyle tenancy = TenancyStyle.Single)
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<Bug6035Happened>();
            opts.Events.AddEventType<Bug6035Noted>();
            opts.Events.RegisterTagType<Bug6035TagId>("bug6078");
            opts.Events.RegisterTagType<Bug6035OtherTag>("bug6078_other");
            opts.Events.DcbStorageMode = mode;
            opts.Events.DcbConflictScope = scope;
            opts.Events.TenancyStyle = tenancy;
        });
    }

    private static IEvent tagged(IDocumentSession session, object data, params object[] tags)
    {
        var e = session.Events.BuildEvent(data);
        e.WithTag(tags);
        return e;
    }

    private IDocumentSession sessionFor(string? tenantId)
        => tenantId == null ? theStore.LightweightSession() : theStore.LightweightSession(tenantId);

    private async Task<Guid> startStream(string? tenantId, object data, params object[] tags)
    {
        var streamId = Guid.NewGuid();
        await using var session = sessionFor(tenantId);
        session.Events.StartStream(streamId, tags.Length == 0 ? data : tagged(session, data, tags));
        await session.SaveChangesAsync();
        return streamId;
    }

    private async Task archive(Guid streamId, string? tenantId = null)
    {
        await using var session = sessionFor(tenantId);
        session.Events.ArchiveStream(streamId);
        await session.SaveChangesAsync();
    }

    private async Task unArchive(Guid streamId, string? tenantId = null)
    {
        await using var session = sessionFor(tenantId);
        session.Events.UnArchiveStream(streamId);
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

    // Every row holding the value, the per-event-type versions included
    private Task<long> rowsAsync(object tagValue)
        => scalarAsync($"select count(*) from {SchemaName}.mt_dcb_tag_version where tag_value = '{tagValue}'");

    private static EventTagQuery queryFor(Bug6035TagId tag, bool byEventType)
    {
        var query = new EventTagQuery().Or(tag);
        return byEventType ? query.AndEventsOfType<Bug6035Happened>() : query;
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables, DcbConflictScope.TagValue, false)]
    [InlineData(DcbStorageMode.HStore, DcbConflictScope.TagValue, false)]
    [InlineData(DcbStorageMode.TagTables, DcbConflictScope.TagValueAndEventType, true)]
    [InlineData(DcbStorageMode.HStore, DcbConflictScope.TagValueAndEventType, true)]
    public async Task a_boundary_that_read_an_archived_stream_conflicts(DcbStorageMode mode, DcbConflictScope scope,
        bool byEventType)
    {
        configure(mode, scope);
        var tag = new Bug6035TagId(Guid.NewGuid());
        var streamId = await startStream(null, new Bug6035Happened("one"), tag);

        await using var decision = theStore.LightweightSession();
        var boundary = await decision.Events.FetchForWritingByTags<Bug6035Boundary>(queryFor(tag, byEventType));
        boundary.Aggregate!.Count.ShouldBe(1);

        // The boundary's events are no longer what a tag read sees
        await archive(streamId);

        boundary.AppendOne(tagged(decision, new Bug6035Happened("two"), tag));
        await Should.ThrowAsync<DcbConcurrencyException>(() => decision.SaveChangesAsync());
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables, DcbConflictScope.TagValue, false)]
    [InlineData(DcbStorageMode.HStore, DcbConflictScope.TagValue, false)]
    [InlineData(DcbStorageMode.TagTables, DcbConflictScope.TagValueAndEventType, true)]
    [InlineData(DcbStorageMode.HStore, DcbConflictScope.TagValueAndEventType, true)]
    public async Task a_boundary_that_read_while_the_stream_was_archived_conflicts_with_the_un_archive(
        DcbStorageMode mode, DcbConflictScope scope, bool byEventType)
    {
        configure(mode, scope);
        var tag = new Bug6035TagId(Guid.NewGuid());
        var streamId = await startStream(null, new Bug6035Happened("one"), tag);
        await archive(streamId);

        await using var decision = theStore.LightweightSession();
        var boundary = await decision.Events.FetchForWritingByTags<Bug6035Boundary>(queryFor(tag, byEventType));
        boundary.Aggregate.ShouldBeNull();

        // The un-archive brings back events the boundary decided without
        await unArchive(streamId);

        boundary.AppendOne(tagged(decision, new Bug6035Happened("two"), tag));
        await Should.ThrowAsync<DcbConcurrencyException>(() => decision.SaveChangesAsync());
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task an_un_archive_of_a_tag_that_never_had_a_row_still_conflicts(DcbStorageMode mode)
    {
        configure(mode);
        var tag = new Bug6035TagId(Guid.NewGuid());
        var streamId = await startStream(null, new Bug6035Happened("one"), tag);
        await archive(streamId);

        // As events from before the version table existed: a tag with events and no row
        await scalarAsync($"with gone as (delete from {SchemaName}.mt_dcb_tag_version returning 1) select count(*) from gone");

        await using var decision = theStore.LightweightSession();
        var boundary = await decision.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(tag));
        boundary.Aggregate.ShouldBeNull();

        await unArchive(streamId);

        boundary.AppendOne(tagged(decision, new Bug6035Happened("two"), tag));
        await Should.ThrowAsync<DcbConcurrencyException>(() => decision.SaveChangesAsync());
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task archiving_moves_every_tag_type_on_the_stream(DcbStorageMode mode)
    {
        configure(mode);
        var tag = new Bug6035TagId(Guid.NewGuid());
        var other = new Bug6035OtherTag("other-" + Guid.NewGuid().ToString("N"));
        var streamId = await startStream(null, new Bug6035Happened("one"), tag, other);

        await using var decision = theStore.LightweightSession();
        var boundary = await decision.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(other));
        boundary.Aggregate!.Count.ShouldBe(1);

        await archive(streamId);

        boundary.AppendOne(tagged(decision, new Bug6035Happened("two"), other));
        await Should.ThrowAsync<DcbConcurrencyException>(() => decision.SaveChangesAsync());
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task archiving_leaves_a_boundary_over_another_tag_value_alone(DcbStorageMode mode)
    {
        configure(mode);
        var archived = new Bug6035TagId(Guid.NewGuid());
        var untouched = new Bug6035TagId(Guid.NewGuid());
        var streamId = await startStream(null, new Bug6035Happened("one"), archived);
        await startStream(null, new Bug6035Happened("one"), untouched);

        await using var decision = theStore.LightweightSession();
        var boundary = await decision.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(untouched));

        await archive(streamId);

        boundary.AppendOne(tagged(decision, new Bug6035Happened("two"), untouched));
        await decision.SaveChangesAsync();
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task under_conjoined_tenancy_only_the_archived_tenant_moves(DcbStorageMode mode)
    {
        configure(mode, tenancy: TenancyStyle.Conjoined);
        var tag = new Bug6035TagId(Guid.NewGuid());
        var redStream = await startStream("red", new Bug6035Happened("one"), tag);
        await startStream("blue", new Bug6035Happened("one"), tag);

        await using var red = theStore.LightweightSession("red");
        var redBoundary = await red.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(tag));
        await using var blue = theStore.LightweightSession("blue");
        var blueBoundary = await blue.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(tag));

        await archive(redStream, "red");

        blueBoundary.AppendOne(tagged(blue, new Bug6035Happened("two"), tag));
        await blue.SaveChangesAsync();

        redBoundary.AppendOne(tagged(red, new Bug6035Happened("two"), tag));
        await Should.ThrowAsync<DcbConcurrencyException>(() => red.SaveChangesAsync());
    }

    [Fact]
    public async Task archiving_an_untagged_stream_still_works()
    {
        configure(DcbStorageMode.TagTables);
        var streamId = await startStream(null, new Bug6035Happened("plain"));

        await archive(streamId);
        await unArchive(streamId);

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamStateAsync(streamId))!.IsArchived.ShouldBeFalse();
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables, DcbConflictScope.TagValue, false)]
    [InlineData(DcbStorageMode.HStore, DcbConflictScope.TagValue, false)]
    [InlineData(DcbStorageMode.TagTables, DcbConflictScope.TagValueAndEventType, true)]
    [InlineData(DcbStorageMode.HStore, DcbConflictScope.TagValueAndEventType, true)]
    public async Task a_boundary_conflicts_with_a_tag_assigned_to_events_it_did_not_read(DcbStorageMode mode,
        DcbConflictScope scope, bool byEventType)
    {
        configure(mode, scope);
        var tag = new Bug6035TagId(Guid.NewGuid());
        await startStream(null, new Bug6035Happened("one"), tag);
        var untagged = await startStream(null, new Bug6035Happened("one"));

        await using var decision = theStore.LightweightSession();
        var boundary = await decision.Events.FetchForWritingByTags<Bug6035Boundary>(queryFor(tag, byEventType));
        boundary.Aggregate!.Count.ShouldBe(1);

        await using (var assign = theStore.LightweightSession())
        {
            assign.Events.AssignTagWhere(x => x.StreamId == untagged, tag);
            await assign.SaveChangesAsync();
        }

        boundary.AppendOne(tagged(decision, new Bug6035Happened("two"), tag));
        await Should.ThrowAsync<DcbConcurrencyException>(() => decision.SaveChangesAsync());
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_boundary_over_a_tag_nothing_carried_conflicts_with_its_first_assignment(DcbStorageMode mode)
    {
        configure(mode);
        var tag = new Bug6035TagId(Guid.NewGuid());
        var untagged = await startStream(null, new Bug6035Happened("one"));

        await using var decision = theStore.LightweightSession();
        var boundary = await decision.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(tag));
        boundary.Aggregate.ShouldBeNull();

        await using (var assign = theStore.LightweightSession())
        {
            assign.Events.AssignTagWhere(x => x.StreamId == untagged, tag);
            await assign.SaveChangesAsync();
        }

        boundary.AppendOne(tagged(decision, new Bug6035Happened("two"), tag));
        await Should.ThrowAsync<DcbConcurrencyException>(() => decision.SaveChangesAsync());
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task an_assignment_that_matches_nothing_writes_no_row(DcbStorageMode mode)
    {
        configure(mode);
        var tag = new Bug6035TagId(Guid.NewGuid());
        var nobody = Guid.NewGuid();

        await using (var assign = theStore.LightweightSession())
        {
            assign.Events.AssignTagWhere(x => x.StreamId == nobody, tag);
            await assign.SaveChangesAsync();
        }

        (await rowsAsync(tag.Value)).ShouldBe(0);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables, DcbConflictScope.TagValue, false)]
    [InlineData(DcbStorageMode.HStore, DcbConflictScope.TagValue, false)]
    [InlineData(DcbStorageMode.TagTables, DcbConflictScope.TagValueAndEventType, true)]
    [InlineData(DcbStorageMode.HStore, DcbConflictScope.TagValueAndEventType, true)]
    public async Task a_boundary_that_read_a_deleted_stream_conflicts(DcbStorageMode mode, DcbConflictScope scope,
        bool byEventType)
    {
        configure(mode, scope);
        var tag = new Bug6035TagId(Guid.NewGuid());
        var doomed = await startStream(null, new Bug6035Happened("one"), tag);
        await startStream(null, new Bug6035Happened("one"), tag);

        await using var decision = theStore.LightweightSession();
        var boundary = await decision.Events.FetchForWritingByTags<Bug6035Boundary>(queryFor(tag, byEventType));
        boundary.Aggregate!.Count.ShouldBe(2);

        await theStore.Advanced.Clean.DeleteSingleEventStreamAsync(doomed);

        boundary.AppendOne(tagged(decision, new Bug6035Happened("two"), tag));
        await Should.ThrowAsync<DcbConcurrencyException>(() => decision.SaveChangesAsync());
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task deleting_a_stream_takes_the_rows_of_tags_no_other_stream_carries(DcbStorageMode mode)
    {
        configure(mode, DcbConflictScope.TagValueAndEventType);
        var mine = new Bug6035TagId(Guid.NewGuid());
        var shared = new Bug6035OtherTag("shared-" + Guid.NewGuid().ToString("N"));
        var doomed = await startStream(null, new Bug6035Happened("one"), mine, shared);
        var survivor = await startStream(null, new Bug6035Happened("one"), shared);

        (await rowsAsync(mine.Value)).ShouldBeGreaterThan(0);

        await theStore.Advanced.Clean.DeleteSingleEventStreamAsync(doomed);

        (await rowsAsync(mine.Value)).ShouldBe(0);
        (await rowsAsync(shared.Value)).ShouldBeGreaterThan(0);

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamStateAsync(doomed)).ShouldBeNull();
        (await query.Events.FetchStreamAsync(survivor)).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task deleting_a_stream_in_one_tenant_leaves_the_other_tenant_alone(DcbStorageMode mode)
    {
        configure(mode, tenancy: TenancyStyle.Conjoined);
        var tag = new Bug6035TagId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        foreach (var tenant in new[] { "red", "blue" })
        {
            await using var session = theStore.LightweightSession(tenant);
            session.Events.StartStream(streamId, tagged(session, new Bug6035Happened("one"), tag));
            await session.SaveChangesAsync();
        }

        await using var red = theStore.LightweightSession("red");
        var redBoundary = await red.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(tag));
        await using var blue = theStore.LightweightSession("blue");
        var blueBoundary = await blue.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(tag));

        await theStore.Advanced.Clean.DeleteSingleEventStreamAsync(streamId, "red");

        blueBoundary.AppendOne(tagged(blue, new Bug6035Happened("two"), tag));
        await blue.SaveChangesAsync();

        redBoundary.AppendOne(tagged(red, new Bug6035Happened("two"), tag));
        await Should.ThrowAsync<DcbConcurrencyException>(() => red.SaveChangesAsync());

        await using var query = theStore.QuerySession("blue");
        (await query.Events.FetchStreamStateAsync(streamId)).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task deleting_a_stream_without_a_tenant_deletes_it_in_every_tenant(DcbStorageMode mode)
    {
        configure(mode, tenancy: TenancyStyle.Conjoined);
        var tag = new Bug6035TagId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        foreach (var tenant in new[] { "red", "blue" })
        {
            await using var session = theStore.LightweightSession(tenant);
            session.Events.StartStream(streamId, tagged(session, new Bug6035Happened("one"), tag));
            await session.SaveChangesAsync();
        }

        await theStore.Advanced.Clean.DeleteSingleEventStreamAsync(streamId);

        (await rowsAsync(tag.Value)).ShouldBe(0);
        (await scalarAsync($"select count(*) from {SchemaName}.mt_streams where id = '{streamId}'")).ShouldBe(0);
        (await scalarAsync($"select count(*) from {SchemaName}.mt_events where stream_id = '{streamId}'")).ShouldBe(0);
    }
}
