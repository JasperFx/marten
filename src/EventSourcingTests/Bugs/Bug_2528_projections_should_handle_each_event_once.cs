using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_2528_projections_should_handle_each_event_once: BugIntegrationContext
{
    [Fact]
    public async Task projection_with_create_method_should_use_create_not_apply()
    {
        var streamId = Guid.NewGuid();
        TheSession.Events.Append(streamId, new IncrementEvent(), new IncrementEvent(), new IncrementEvent());
        await TheSession.SaveChangesAsync();

        var aggregate = await TheSession.Events.AggregateStreamAsync<CounterWithCreate>(streamId);

        Assert.NotNull(aggregate);
        Assert.Equal(streamId, aggregate.Id);
        Assert.Equal(1, aggregate.CreateCounter);
        Assert.Equal(2, aggregate.ApplyCounter);
    }

    [Fact]
    public async Task projection_with_default_ctor_should_use_apply()
    {
        var streamId = Guid.NewGuid();
        TheSession.Events.Append(streamId, new IncrementEvent(), new IncrementEvent(), new IncrementEvent());
        await TheSession.SaveChangesAsync();

        var aggregate = await TheSession.Events.AggregateStreamAsync<CounterWithDefaultCtor>(streamId);

        Assert.NotNull(aggregate);
        Assert.Equal(streamId, aggregate.Id);
        Assert.Equal(0, aggregate.CreateCounter);
        Assert.Equal(3, aggregate.ApplyCounter);
    }

    [Fact]
    public async Task projection_with_create_and_ctor_should_use_create_on_event_match()
    {
        var streamId = Guid.NewGuid();
        TheSession.Events.Append(streamId, new IncrementEvent(), new IncrementEvent(), new IncrementEvent());
        await TheSession.SaveChangesAsync();

        var aggregate = await TheSession.Events.AggregateStreamAsync<CounterWithCreateAndDefaultCtor>(streamId);

        Assert.NotNull(aggregate);
        Assert.Equal(streamId, aggregate.Id);
        Assert.Equal(1, aggregate.CreateCounter);
        Assert.Equal(2, aggregate.ApplyCounter);
    }

    [Fact]
    public async Task projection_with_create_and_ctor_should_use_default_ctor_on_no_event_match()
    {
        var streamId = Guid.NewGuid();
        TheSession.Events.Append(streamId, new UnrelatedEvent(), new IncrementEvent(), new IncrementEvent(), new IncrementEvent());
        await TheSession.SaveChangesAsync();

        var aggregate = await TheSession.Events.AggregateStreamAsync<CounterWithCreateAndDefaultCtor>(streamId);

        Assert.NotNull(aggregate);
        Assert.Equal(streamId, aggregate.Id);
        Assert.Equal(0, aggregate.CreateCounter);
        Assert.Equal(3, aggregate.ApplyCounter);
    }

    public record IncrementEvent;
    public record UnrelatedEvent;

    public record CounterWithCreate(Guid Id, int CreateCounter, int ApplyCounter)
    {
        public static CounterWithCreate Create(IncrementEvent @event, IEvent metadata)
        {
            return new CounterWithCreate(metadata.StreamId, 1, 0);
        }

        public CounterWithCreate Apply(IncrementEvent @event, CounterWithCreate current)
        {
            return current with { ApplyCounter = current.ApplyCounter + 1};
        }
    }

    public record CounterWithDefaultCtor
    {
        public CounterWithDefaultCtor()
        {
            // will be initialized through reflection
            Id = Guid.Empty;
            CreateCounter = 0;
            ApplyCounter = 0;
        }

        public CounterWithDefaultCtor Apply(IncrementEvent @event, CounterWithDefaultCtor current)
        {
            return current with { ApplyCounter = current.ApplyCounter + 1 };
        }

        public Guid Id { get; init; }
        public int CreateCounter { get; init; }
        public int ApplyCounter { get; init; }
    }

    public record CounterWithCreateAndDefaultCtor(Guid Id, int CreateCounter, int ApplyCounter)
    {
        public CounterWithCreateAndDefaultCtor(): this(Guid.Empty, 0, 0)
        {
        }

        public static CounterWithCreateAndDefaultCtor Create(IncrementEvent @event, IEvent metadata)
        {
            return new CounterWithCreateAndDefaultCtor(metadata.StreamId, 1, 0);
        }

        public CounterWithCreateAndDefaultCtor Apply(IncrementEvent @event, CounterWithCreateAndDefaultCtor current)
        {
            return current with { ApplyCounter = current.ApplyCounter + 1 };
        }
    }
}
