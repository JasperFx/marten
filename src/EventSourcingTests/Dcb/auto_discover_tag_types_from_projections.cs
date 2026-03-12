#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.Events.Tags;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Dcb;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

// Strong-typed identifier for auto-discovery tests
public record struct TicketId(Guid Value);

// Domain events
public record TicketOpened(string Title);
public record TicketResolved(string Resolution);

// Aggregate that uses the strong-typed ID as its document identity
public class TicketSummary
{
    public TicketId Id { get; set; }
    public string Title { get; set; } = "";
    public string? Resolution { get; set; }

    public void Apply(TicketOpened e) => Title = e.Title;
    public void Apply(TicketResolved e) => Resolution = e.Resolution;
}

// Projection with strong-typed ID — auto-discovery should detect TicketId from TId
public class TicketSummaryProjection: SingleStreamProjection<TicketSummary, TicketId>
{
}

[Collection("OneOffs")]
public class auto_discover_tag_types_from_projections: OneOffConfigurationsContext, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void tag_type_is_auto_registered_from_single_stream_projection()
    {
        // Register the projection but do NOT explicitly call RegisterTagType<TicketId>()
        StoreOptions(opts =>
        {
            opts.Projections.Add<TicketSummaryProjection>(ProjectionLifecycle.Inline);
        });

        // The tag type should have been auto-discovered from the projection's TId
        var registration = theStore.Events.FindTagType(typeof(TicketId));
        registration.ShouldNotBeNull();
        registration.TagType.ShouldBe(typeof(TicketId));
        registration.AggregateType.ShouldBe(typeof(TicketSummary));
    }

    [Fact]
    public void explicit_registration_takes_precedence_over_auto_discovery()
    {
        // Explicitly register with a custom table suffix BEFORE auto-discovery runs
        StoreOptions(opts =>
        {
            opts.Events.RegisterTagType<TicketId>("custom_ticket")
                .ForAggregate<TicketSummary>();
            opts.Projections.Add<TicketSummaryProjection>(ProjectionLifecycle.Inline);
        });

        var registration = theStore.Events.FindTagType(typeof(TicketId));
        registration.ShouldNotBeNull();
        registration.TableSuffix.ShouldBe("custom_ticket");
    }

    [Fact]
    public void primitive_identity_types_are_not_auto_registered()
    {
        // A projection with a Guid identity (primitive) should NOT auto-register a tag type
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<StudentCourseEnrollment>();
        });

        theStore.Events.FindTagType(typeof(Guid)).ShouldBeNull();
    }

    [Fact]
    public async Task auto_discovered_tag_type_works_for_querying()
    {
        // Register projection only — no explicit RegisterTagType call
        StoreOptions(opts =>
        {
            opts.Projections.Add<TicketSummaryProjection>(ProjectionLifecycle.Inline);
            opts.Events.AddEventType<TicketOpened>();
            opts.Events.AddEventType<TicketResolved>();
        });

        var ticketId = new TicketId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var opened = theSession.Events.BuildEvent(new TicketOpened("Fix bug"));
        opened.WithTag(ticketId);
        theSession.Events.Append(streamId, opened);
        await theSession.SaveChangesAsync();

        var query = new EventTagQuery().Or<TicketId>(ticketId);
        var events = await theSession.Events.QueryByTagsAsync(query);
        events.Count.ShouldBe(1);
        events[0].Data.ShouldBeOfType<TicketOpened>();
    }

    [Fact]
    public async Task auto_discovered_tag_type_works_for_fetch_for_writing()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<TicketSummaryProjection>(ProjectionLifecycle.Inline);
            opts.Events.AddEventType<TicketOpened>();
            opts.Events.AddEventType<TicketResolved>();
        });

        var ticketId = new TicketId(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        var opened = theSession.Events.BuildEvent(new TicketOpened("Add feature"));
        opened.WithTag(ticketId);
        theSession.Events.Append(streamId, opened);
        await theSession.SaveChangesAsync();

        var query = new EventTagQuery().Or<TicketId>(ticketId);
        var boundary = await theSession.Events.FetchForWritingByTags<TicketSummary>(query);

        boundary.Aggregate.ShouldNotBeNull();
        boundary.Aggregate.Title.ShouldBe("Add feature");
        boundary.Events.Count.ShouldBe(1);
    }
}
