using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Operations;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public class assert_stream_version: IntegrationContext
{
    public assert_stream_version(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override Task fixtureSetup()
    {
        return theStore.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    [Fact]
    public async Task happy_path_version_matches_Guid_identifier()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        // Build a StreamAction that matches the current version
        var stream = new StreamAction(streamId, StreamActionType.Append)
        {
            ExpectedVersionOnServer = 3,
            AggregateType = typeof(SimpleAggregate)
        };

        var operation = new AssertStreamVersionById(theStore.Options.EventGraph, stream);

        // Execute in a batch - should not throw
        theSession.QueueOperation(operation);
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task happy_path_version_matches_string_identifier()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var streamKey = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamKey, new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        // Build a StreamAction that matches the current version
        var stream = new StreamAction(streamKey, StreamActionType.Append)
        {
            ExpectedVersionOnServer = 3,
            AggregateType = typeof(SimpleAggregateAsString)
        };

        var operation = new AssertStreamVersionByKey(theStore.Options.EventGraph, stream);

        // Execute in a batch - should not throw
        theSession.QueueOperation(operation);
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task sad_path_version_mismatch_Guid_identifier()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        // Build a StreamAction with the WRONG expected version
        var stream = new StreamAction(streamId, StreamActionType.Append)
        {
            ExpectedVersionOnServer = 2, // actual is 3
            AggregateType = typeof(SimpleAggregate)
        };

        var operation = new AssertStreamVersionById(theStore.Options.EventGraph, stream);

        theSession.QueueOperation(operation);

        var ex = await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });

        ex.ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>();
        ex.Message.ShouldContain(streamId.ToString());
        ex.Message.ShouldContain("expected 2");
        ex.Message.ShouldContain("was 3");
    }

    [Fact]
    public async Task sad_path_version_mismatch_string_identifier()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var streamKey = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamKey, new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        // Build a StreamAction with the WRONG expected version
        var stream = new StreamAction(streamKey, StreamActionType.Append)
        {
            ExpectedVersionOnServer = 2, // actual is 3
            AggregateType = typeof(SimpleAggregateAsString)
        };

        var operation = new AssertStreamVersionByKey(theStore.Options.EventGraph, stream);

        theSession.QueueOperation(operation);

        var ex = await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });

        ex.ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>();
        ex.Message.ShouldContain(streamKey);
        ex.Message.ShouldContain("expected 2");
        ex.Message.ShouldContain("was 3");
    }

    [Fact]
    public async Task sad_path_stream_does_not_exist_Guid_identifier()
    {
        var streamId = Guid.NewGuid();

        // Build a StreamAction for a non-existent stream
        var stream = new StreamAction(streamId, StreamActionType.Append)
        {
            ExpectedVersionOnServer = 1,
            AggregateType = typeof(SimpleAggregate)
        };

        var operation = new AssertStreamVersionById(theStore.Options.EventGraph, stream);

        theSession.QueueOperation(operation);

        var ex = await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });

        ex.ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>();
    }

    [Fact]
    public async Task sad_path_stream_does_not_exist_string_identifier()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var streamKey = Guid.NewGuid().ToString();

        // Build a StreamAction for a non-existent stream
        var stream = new StreamAction(streamKey, StreamActionType.Append)
        {
            ExpectedVersionOnServer = 1,
            AggregateType = typeof(SimpleAggregateAsString)
        };

        var operation = new AssertStreamVersionByKey(theStore.Options.EventGraph, stream);

        theSession.QueueOperation(operation);

        var ex = await Should.ThrowAsync<ConcurrencyException>(async () =>
        {
            await theSession.SaveChangesAsync();
        });

        ex.ShouldBeOfType<EventStreamUnexpectedMaxEventIdException>();
    }
}
