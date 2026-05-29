#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

// Strong-typed tag identifiers for the boundary-aggregate scenario
public record EnrolleeId(Guid Value);
public record ProgramId(Guid Value);

public record Enrolled(string Name);
public record ProgressRecorded(string Milestone);

#region sample_marten_dcb_boundary_aggregate
// A *pure* DCB boundary aggregate: Apply methods, but no single-stream identity
// (no Id property, no [AggregateIdentity]). It spans multiple streams by tag, so
// the only thing that makes the source generator emit an evolver for it is the
// [BoundaryAggregate] marker. See marten#4510 / jasperfx#324.
[BoundaryAggregate]
public class SubscriptionState
{
    public int EnrollmentCount { get; set; }
    public int ProgressCount { get; set; }

    public void Apply(Enrolled _) => EnrollmentCount++;
    public void Apply(ProgressRecorded _) => ProgressCount++;
}
#endregion

[Collection("OneOffs")]
public class dcb_boundary_aggregate_fetch_for_writing_tests: OneOffConfigurationsContext, IAsyncLifetime
{
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;

            opts.Events.AddEventType<Enrolled>();
            opts.Events.AddEventType<ProgressRecorded>();

            // Pure boundary aggregate: registered only via ForAggregate<T>(), with
            // no LiveStreamAggregation / Snapshot (which would require a stream Id).
            opts.Events.RegisterTagType<EnrolleeId>("enrollee").ForAggregate<SubscriptionState>();
            opts.Events.RegisterTagType<ProgramId>("program").ForAggregate<SubscriptionState>();
        });
    }

    public Task InitializeAsync()
    {
        ConfigureStore();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task fetch_for_writing_by_tags_works_for_pure_boundary_aggregate()
    {
        var enrolleeId = new EnrolleeId(Guid.NewGuid());
        var programId = new ProgramId(Guid.NewGuid());

        // Seed two tagged events across (logically) different streams
        var enrolled = theSession.Events.BuildEvent(new Enrolled("Alice"));
        enrolled.WithTag(enrolleeId, programId);
        theSession.Events.Append(Guid.NewGuid().ToString(), enrolled);

        var progress = theSession.Events.BuildEvent(new ProgressRecorded("Module 1"));
        progress.WithTag(enrolleeId);
        theSession.Events.Append(Guid.NewGuid().ToString(), progress);

        await theSession.SaveChangesAsync();

        // Before jasperfx#324 this threw InvalidProjectionException
        // "No source-generated dispatcher found for SingleStreamProjection<SubscriptionState, string>".
        await using var session = theStore.LightweightSession();
        var query = new EventTagQuery().Or<EnrolleeId>(enrolleeId);
        var boundary = await session.Events.FetchForWritingByTags<SubscriptionState>(query);

        boundary.Aggregate.ShouldNotBeNull();
        boundary.Aggregate!.EnrollmentCount.ShouldBe(1);
        boundary.Aggregate.ProgressCount.ShouldBe(1);
        boundary.Events.Count.ShouldBe(2);
    }
}

// Same setup as the sibling fixture but without overriding StreamIdentity,
// covering the default AsGuid path.
[Collection("OneOffs")]
public class dcb_boundary_aggregate_default_stream_identity_tests: OneOffConfigurationsContext, IAsyncLifetime
{
    private void ConfigureStore()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<Enrolled>();
            opts.Events.AddEventType<ProgressRecorded>();

            opts.Events.RegisterTagType<EnrolleeId>("enrollee").ForAggregate<SubscriptionState>();
            opts.Events.RegisterTagType<ProgramId>("program").ForAggregate<SubscriptionState>();
        });
    }

    public Task InitializeAsync()
    {
        ConfigureStore();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task fetch_for_writing_by_tags_works_with_default_guid_stream_identity()
    {
        var enrolleeId = new EnrolleeId(Guid.NewGuid());

        var enrolled = theSession.Events.BuildEvent(new Enrolled("Bob"));
        enrolled.WithTag(enrolleeId);
        theSession.Events.Append(Guid.NewGuid(), enrolled);

        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        var query = new EventTagQuery().Or<EnrolleeId>(enrolleeId);
        var boundary = await session.Events.FetchForWritingByTags<SubscriptionState>(query);

        boundary.Aggregate.ShouldNotBeNull();
        boundary.Aggregate!.EnrollmentCount.ShouldBe(1);
    }
}
