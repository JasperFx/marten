using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3078_providing_version
{
    [Fact]
    public async Task too_high_should_aggregate_to_null()
    {
        // Given
        var streamId = Guid.NewGuid();
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);
        options.Projections.LiveStreamAggregation<PolledAggregate>();

        // When
        var store = new DocumentStore(options);
        var session = store.LightweightSession();
        session.Events.Append(streamId, new PollingShouldFail(streamId));
        await session.SaveChangesAsync();

        // Then
        var aggregate = await session.Events.AggregateStreamAsync<PolledAggregate>(streamId, 2);
        aggregate.ShouldBeNull();
    }

    [Fact]
    public async Task exactly_right_should_aggregate_just_fine()
    {
        // Given
        var streamId = Guid.NewGuid();
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);
        options.Projections.LiveStreamAggregation<PolledAggregate>();

        // When
        var store = new DocumentStore(options);
        var session = store.LightweightSession();
        session.Events.Append(streamId, new PollingShouldFail(streamId), new PollingShouldNotFail());
        await session.SaveChangesAsync();

        // Then
        var aggregate = await session.Events.AggregateStreamAsync<PolledAggregate>(streamId, 2);
        aggregate.ShouldNotBeNull();
    }
}

public record PollingShouldFail(Guid AggregateId);

public record PollingShouldNotFail();

public record PolledAggregate(Guid Id)
{
    public static PolledAggregate Create(PollingShouldFail @event) => new(@event.AggregateId);

    public PolledAggregate Apply(PollingShouldNotFail _) => this;
}
