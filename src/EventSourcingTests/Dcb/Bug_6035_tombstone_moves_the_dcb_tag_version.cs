#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

public record Bug6035TagId(Guid Value);
public record Bug6035OtherTag(string Value);
public record Bug6035Happened(string Name);
public record Bug6035Noted(string Name);

[BoundaryAggregate]
public class Bug6035Boundary
{
    public int Count { get; set; }
    public void Apply(Bug6035Happened _) => Count++;
    public void Apply(Bug6035Noted _) => Count++;
}

public class Bug_6035_tombstone_moves_the_dcb_tag_version: OneOffConfigurationsContext
{
    private void configure(DcbStorageMode mode, DcbConflictScope scope = DcbConflictScope.TagValue,
        TenancyStyle tenancy = TenancyStyle.Single)
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<Bug6035Happened>();
            opts.Events.AddEventType<Bug6035Noted>();
            opts.Events.RegisterTagType<Bug6035TagId>("bug6035");
            opts.Events.RegisterTagType<Bug6035OtherTag>("bug6035_other");
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

    private async Task<Guid> startTaggedStream(string? tenantId, params object[] tags)
    {
        var streamId = Guid.NewGuid();
        await using var session = tenantId == null ? theStore.LightweightSession() : theStore.LightweightSession(tenantId);
        session.Events.StartStream(streamId, tagged(session, new Bug6035Happened("one"), tags));
        await session.SaveChangesAsync();
        return streamId;
    }

    private async Task tombstone(Guid streamId, string? tenantId = null)
    {
        await using var session = tenantId == null ? theStore.LightweightSession() : theStore.LightweightSession(tenantId);
        session.Events.TombstoneStream(streamId);
        await session.SaveChangesAsync();
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables, DcbConflictScope.TagValue, false)]
    [InlineData(DcbStorageMode.HStore, DcbConflictScope.TagValue, false)]
    [InlineData(DcbStorageMode.TagTables, DcbConflictScope.TagValueAndEventType, false)]
    [InlineData(DcbStorageMode.HStore, DcbConflictScope.TagValueAndEventType, false)]
    [InlineData(DcbStorageMode.TagTables, DcbConflictScope.TagValueAndEventType, true)]
    [InlineData(DcbStorageMode.HStore, DcbConflictScope.TagValueAndEventType, true)]
    public async Task a_boundary_that_read_a_tombstoned_stream_conflicts(DcbStorageMode mode, DcbConflictScope scope,
        bool byEventType)
    {
        configure(mode, scope);

        var tag = new Bug6035TagId(Guid.NewGuid());
        var streamId = await startTaggedStream(null, tag);

        var query = new EventTagQuery().Or(tag);
        if (byEventType) query = query.AndEventsOfType<Bug6035Happened>();

        await using var decision = theStore.LightweightSession();
        var boundary = await decision.Events.FetchForWritingByTags<Bug6035Boundary>(query);
        boundary.Aggregate!.Count.ShouldBe(1);

        await tombstone(streamId);

        boundary.AppendOne(tagged(decision, new Bug6035Happened("two"), tag));
        await Should.ThrowAsync<DcbConcurrencyException>(() => decision.SaveChangesAsync());
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task every_tag_type_on_the_stream_moves(DcbStorageMode mode)
    {
        configure(mode);

        var tag = new Bug6035TagId(Guid.NewGuid());
        var other = new Bug6035OtherTag("other-" + Guid.NewGuid().ToString("N"));
        var streamId = await startTaggedStream(null, tag, other);

        await using var decision = theStore.LightweightSession();
        var boundary = await decision.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(other));
        boundary.Aggregate!.Count.ShouldBe(1);

        await tombstone(streamId);

        boundary.AppendOne(tagged(decision, new Bug6035Happened("two"), other));
        await Should.ThrowAsync<DcbConcurrencyException>(() => decision.SaveChangesAsync());
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task a_boundary_over_another_tag_value_is_not_disturbed(DcbStorageMode mode)
    {
        configure(mode);

        var doomed = new Bug6035TagId(Guid.NewGuid());
        var untouched = new Bug6035TagId(Guid.NewGuid());
        var streamId = await startTaggedStream(null, doomed);
        await startTaggedStream(null, untouched);

        await using var decision = theStore.LightweightSession();
        var boundary = await decision.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(untouched));

        await tombstone(streamId);

        boundary.AppendOne(tagged(decision, new Bug6035Happened("two"), untouched));
        await decision.SaveChangesAsync();
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task under_conjoined_tenancy_only_the_tombstoned_tenant_moves(DcbStorageMode mode)
    {
        configure(mode, tenancy: TenancyStyle.Conjoined);

        var tag = new Bug6035TagId(Guid.NewGuid());
        var redStream = await startTaggedStream("red", tag);
        await startTaggedStream("blue", tag);

        await using var red = theStore.LightweightSession("red");
        var redBoundary = await red.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(tag));
        await using var blue = theStore.LightweightSession("blue");
        var blueBoundary = await blue.Events.FetchForWritingByTags<Bug6035Boundary>(new EventTagQuery().Or(tag));

        await tombstone(redStream, "red");

        blueBoundary.AppendOne(tagged(blue, new Bug6035Happened("two"), tag));
        await blue.SaveChangesAsync();

        redBoundary.AppendOne(tagged(red, new Bug6035Happened("two"), tag));
        await Should.ThrowAsync<DcbConcurrencyException>(() => red.SaveChangesAsync());
    }

    [Fact]
    public async Task tombstoning_an_untagged_stream_still_works()
    {
        configure(DcbStorageMode.TagTables);

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new Bug6035Happened("plain"));
            await session.SaveChangesAsync();
        }

        await tombstone(streamId);

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamStateAsync(streamId)).ShouldBeNull();
    }
}
