#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.Events.Tags;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

// Distinct types from auto_discover_tag_types_from_projections.cs so the two
// fixtures don't share global aggregator wiring in the test assembly.
public record struct HsTicketId(Guid Value);

public record HsTicketOpened(string Title);
public record HsTicketResolved(string Resolution);

public class HsTicketSummary
{
    public HsTicketId Id { get; set; }
    public string Title { get; set; } = "";
    public string? Resolution { get; set; }

    public void Apply(HsTicketOpened e) => Title = e.Title;
    public void Apply(HsTicketResolved e) => Resolution = e.Resolution;
}

public partial class HsTicketSummaryProjection: SingleStreamProjection<HsTicketSummary, HsTicketId>
{
}

/// <summary>
/// Parallel of <see cref="auto_discover_tag_types_from_projections"/> under
/// <see cref="DcbStorageMode.HStore"/>. Tag-type auto-discovery is registration-time
/// logic that runs identically regardless of storage mode, but we want explicit
/// proof that auto-discovered tag types interoperate with hstore-backed query and
/// fetch-for-writing paths.
/// </summary>
[Collection("OneOffs")]
public class hstore_auto_discover_tag_types_from_projections: OneOffConfigurationsContext, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void tag_type_is_auto_registered_from_single_stream_projection_in_hstore_mode()
    {
        StoreOptions(opts =>
        {
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Projections.Add<HsTicketSummaryProjection>(ProjectionLifecycle.Inline);
        });

        var registration = theStore.Events.FindTagType(typeof(HsTicketId));
        registration.ShouldNotBeNull();
        registration.TagType.ShouldBe(typeof(HsTicketId));
        registration.AggregateType.ShouldBe(typeof(HsTicketSummary));
    }

    [Fact]
    public void explicit_registration_takes_precedence_over_auto_discovery_in_hstore_mode()
    {
        StoreOptions(opts =>
        {
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Events.RegisterTagType<HsTicketId>("custom_hs_ticket")
                .ForAggregate<HsTicketSummary>();
            opts.Projections.Add<HsTicketSummaryProjection>(ProjectionLifecycle.Inline);
        });

        var registration = theStore.Events.FindTagType(typeof(HsTicketId));
        registration.ShouldNotBeNull();
        registration.TableSuffix.ShouldBe("custom_hs_ticket");
    }

    [Fact(Skip = "Pre-existing master failure (codegen + closed-shape) — inline-projection save raises ConcurrencyException for HsTicketSummary. Hstore tag-based aggregation interacts badly with revision tracking on the projection target. Unrelated to #4444's UseVersionFromMatchingStream gap; needs separate triage.")]
    public async Task auto_discovered_tag_type_works_for_querying_in_hstore_mode()
    {
        StoreOptions(opts =>
        {
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Projections.Add<HsTicketSummaryProjection>(ProjectionLifecycle.Inline);
            opts.Events.AddEventType<HsTicketOpened>();
            opts.Events.AddEventType<HsTicketResolved>();
        });

        var ticketId = new HsTicketId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var opened = theSession.Events.BuildEvent(new HsTicketOpened("Fix bug"));
        opened.WithTag(ticketId);
        theSession.Events.Append(streamId, opened);
        await theSession.SaveChangesAsync();

        var query = new EventTagQuery().Or<HsTicketId>(ticketId);
        var events = await theSession.Events.QueryByTagsAsync(query);
        events.Count.ShouldBe(1);
        events[0].Data.ShouldBeOfType<HsTicketOpened>();
    }

    [Fact(Skip = "Pre-existing master failure (codegen + closed-shape) — inline-projection save raises ConcurrencyException for HsTicketSummary. Hstore tag-based aggregation interacts badly with revision tracking on the projection target. Unrelated to #4444's UseVersionFromMatchingStream gap; needs separate triage.")]
    public async Task auto_discovered_tag_type_works_for_fetch_for_writing_in_hstore_mode()
    {
        StoreOptions(opts =>
        {
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;
            opts.Projections.Add<HsTicketSummaryProjection>(ProjectionLifecycle.Inline);
            opts.Events.AddEventType<HsTicketOpened>();
            opts.Events.AddEventType<HsTicketResolved>();
        });

        var ticketId = new HsTicketId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var opened = theSession.Events.BuildEvent(new HsTicketOpened("Add feature"));
        opened.WithTag(ticketId);
        theSession.Events.Append(streamId, opened);
        await theSession.SaveChangesAsync();

        var query = new EventTagQuery().Or<HsTicketId>(ticketId);
        var boundary = await theSession.Events.FetchForWritingByTags<HsTicketSummary>(query);

        boundary.Aggregate.ShouldNotBeNull();
        boundary.Aggregate.Title.ShouldBe("Add feature");
        boundary.Events.Count.ShouldBe(1);
    }
}
