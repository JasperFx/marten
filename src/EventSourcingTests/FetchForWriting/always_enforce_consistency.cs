using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public class always_enforce_consistency: IntegrationContext
{
    public always_enforce_consistency(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override Task fixtureSetup()
    {
        return theStore.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    [Fact]
    public async Task default_value_is_false_Guid_identifier()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AlwaysEnforceConsistency.ShouldBeFalse();
    }

    [Fact]
    public async Task default_value_is_false_string_identifier()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var streamKey = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamKey, new AEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamKey);
        stream.AlwaysEnforceConsistency.ShouldBeFalse();
    }

    [Fact]
    public async Task save_changes_without_events_and_no_consistency_flag_does_not_throw_Guid_identifier()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);

        // Do not append any events, do not set AlwaysEnforceConsistency
        // This should succeed silently
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task enforce_consistency_happy_path_no_events_Guid_identifier()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AlwaysEnforceConsistency = true;

        // Don't append events - should still succeed because version hasn't changed
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task enforce_consistency_happy_path_no_events_string_identifier()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var streamKey = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamKey, new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamKey);
        stream.AlwaysEnforceConsistency = true;

        // Don't append events - should still succeed because version hasn't changed
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task enforce_consistency_sad_path_no_events_Guid_identifier()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AlwaysEnforceConsistency = true;

        // Sneak in events from another session
        await using (var otherSession = theStore.LightweightSession())
        {
            otherSession.Events.Append(streamId, new DEvent());
            await otherSession.SaveChangesAsync();
        }

        // Now save should fail - version has advanced
        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task enforce_consistency_sad_path_no_events_string_identifier()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var streamKey = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamKey, new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamKey);
        stream.AlwaysEnforceConsistency = true;

        // Sneak in events from another session
        await using (var otherSession = theStore.LightweightSession())
        {
            otherSession.Events.Append(streamKey, new DEvent());
            await otherSession.SaveChangesAsync();
        }

        // Now save should fail - version has advanced
        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task enforce_consistency_with_events_works_as_before_Guid_identifier()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AlwaysEnforceConsistency = true;

        // Append events - this should use the normal UpdateStreamVersion path
        stream.AppendOne(new CEvent());
        await theSession.SaveChangesAsync();

        // Verify the event was stored
        var aggregate = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task enforce_consistency_with_events_works_as_before_string_identifier()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var streamKey = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamKey, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamKey);
        stream.AlwaysEnforceConsistency = true;

        // Append events - this should use the normal UpdateStreamVersion path
        stream.AppendOne(new CEvent());
        await theSession.SaveChangesAsync();

        // Verify the event was stored
        var aggregate = await theSession.Events.AggregateStreamAsync<SimpleAggregateAsString>(streamKey);
        aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task enforce_consistency_with_events_sad_path_Guid_identifier()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregate>(streamId);
        stream.AlwaysEnforceConsistency = true;

        // Append events
        stream.AppendOne(new CEvent());

        // Sneak in events from another session
        await using (var otherSession = theStore.LightweightSession())
        {
            otherSession.Events.Append(streamId, new DEvent());
            await otherSession.SaveChangesAsync();
        }

        // Save should fail because another session advanced the version
        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task enforce_consistency_with_events_sad_path_string_identifier()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var streamKey = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamKey, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        var stream = await theSession.Events.FetchForWriting<SimpleAggregateAsString>(streamKey);
        stream.AlwaysEnforceConsistency = true;

        // Append events
        stream.AppendOne(new CEvent());

        // Sneak in events from another session
        await using (var otherSession = theStore.LightweightSession())
        {
            otherSession.Events.Append(streamKey, new DEvent());
            await otherSession.SaveChangesAsync();
        }

        // Save should fail because another session advanced the version
        await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });
    }
}
