using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections;

#region sample_project_latest_events_and_aggregate

public record ReportCreated(string Title);
public record SectionAdded(string SectionName);
public record ReportPublished;

public class Report
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public int SectionCount { get; set; }
    public bool IsPublished { get; set; }

    // Self-aggregating methods for LiveStreamAggregation
    public static Report Create(ReportCreated e) => new Report { Title = e.Title };
    public void Apply(SectionAdded e) => SectionCount++;
    public void Apply(ReportPublished e) => IsPublished = true;
}

public class ReportProjection : SingleStreamProjection<Report, Guid>
{
    public Report Create(ReportCreated e) => new Report { Title = e.Title };

    public void Apply(SectionAdded e, Report report) => report.SectionCount++;

    public void Apply(ReportPublished e, Report report) => report.IsPublished = true;
}

#endregion

public class project_latest_tests : OneOffConfigurationsContext
{
    [Fact]
    public async Task live_projection_includes_pending_events()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<Report>();
        });

        var streamId = Guid.NewGuid();

        await using var session = theStore.LightweightSession();

        // Append events but do NOT save
        session.Events.StartStream(streamId,
            new ReportCreated("Q1 Report"),
            new SectionAdded("Revenue"),
            new SectionAdded("Costs")
        );

        // ProjectLatest should include the pending events
        var report = await session.Events.ProjectLatest<Report>(streamId);

        report.ShouldNotBeNull();
        report.Title.ShouldBe("Q1 Report");
        report.SectionCount.ShouldBe(2);
    }

    [Fact]
    public async Task inline_projection_includes_pending_events()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<ReportProjection>(ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();

        await using var session = theStore.LightweightSession();

        // Append events but do NOT save
        session.Events.StartStream(streamId,
            new ReportCreated("Q1 Report"),
            new SectionAdded("Revenue")
        );

        // ProjectLatest should include the pending events
        var report = await session.Events.ProjectLatest<Report>(streamId);

        report.ShouldNotBeNull();
        report.Title.ShouldBe("Q1 Report");
        report.SectionCount.ShouldBe(1);
    }

    [Fact]
    public async Task inline_projection_stores_document_on_project_latest()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<ReportProjection>(ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();

        // First, save some initial events
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new ReportCreated("Q1 Report"),
                new SectionAdded("Revenue")
            );
            await session.SaveChangesAsync();
        }

        // Now open a new session, append more events, and call ProjectLatest
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId,
                new SectionAdded("Costs"),
                new ReportPublished()
            );

            var report = await session.Events.ProjectLatest<Report>(streamId);

            report.ShouldNotBeNull();
            report.SectionCount.ShouldBe(2); // Revenue + Costs
            report.IsPublished.ShouldBeTrue();

            // Now save - the inline-projected document should be updated
            await session.SaveChangesAsync();
        }

        // Verify the document was persisted with the projected state
        await using (var query = theStore.QuerySession())
        {
            var report = await query.LoadAsync<Report>(streamId);
            report.ShouldNotBeNull();
            report.SectionCount.ShouldBe(2);
            report.IsPublished.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task no_pending_events_behaves_like_fetch_latest()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<ReportProjection>(ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new ReportCreated("Q1 Report"),
                new SectionAdded("Revenue")
            );
            await session.SaveChangesAsync();
        }

        // No pending events in this session
        await using (var session = theStore.LightweightSession())
        {
            var fromProjectLatest = await session.Events.ProjectLatest<Report>(streamId);
            var fromFetchLatest = await session.Events.FetchLatest<Report>(streamId);

            fromProjectLatest.ShouldNotBeNull();
            fromFetchLatest.ShouldNotBeNull();
            fromProjectLatest.Title.ShouldBe(fromFetchLatest.Title);
            fromProjectLatest.SectionCount.ShouldBe(fromFetchLatest.SectionCount);
        }
    }

    [Fact]
    public async Task live_projection_with_committed_and_pending_events()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<Report>();
        });

        var streamId = Guid.NewGuid();

        // First commit some events
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new ReportCreated("Q1 Report"),
                new SectionAdded("Revenue")
            );
            await session.SaveChangesAsync();
        }

        // Now append more without saving and project
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId,
                new SectionAdded("Costs"),
                new SectionAdded("Outlook"),
                new ReportPublished()
            );

            var report = await session.Events.ProjectLatest<Report>(streamId);

            report.ShouldNotBeNull();
            report.Title.ShouldBe("Q1 Report");
            report.SectionCount.ShouldBe(3); // Revenue + Costs + Outlook
            report.IsPublished.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task returns_null_for_nonexistent_stream_with_no_pending_events()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<Report>();
        });

        await using var session = theStore.LightweightSession();

        var report = await session.Events.ProjectLatest<Report>(Guid.NewGuid());
        report.ShouldBeNull();
    }
}
