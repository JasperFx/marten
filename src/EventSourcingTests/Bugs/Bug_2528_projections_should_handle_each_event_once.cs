using System;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;

namespace EventSourcingTests.Bugs;

public class Bug_2528_projections_should_handle_each_event_once: BugIntegrationContext
{
    [Fact]
    public async Task projection_with_create_method_should_use_apply_in_later_transaction()
    {
        var streamId = Guid.NewGuid();

        var store = StoreOptions(o =>
        {
            o.Projections.Add<CounterWithDefaultCtorProjection>(ProjectionLifecycle.Inline);
            o.GeneratedCodeMode = TypeLoadMode.Auto;
        });

        var first = store.LightweightSession();
        first.Events.Append(streamId,
            new IncrementEventWithId(streamId),
            new IncrementEventWithId(streamId),
            new IncrementEventWithId(streamId));
        await first.SaveChangesAsync();

        var readModel1 = first.Load<CounterWithCreate>(streamId);

        readModel1.ShouldNotBeNull();
        readModel1.Id.ShouldBe(streamId);
        readModel1.CreateCounter.ShouldBe(1);
        // Fails: ApplyCounter is 1.

        readModel1.ApplyCounter.ShouldBe(2);

        var session2 = store.LightweightSession();
        session2.Events.Append(streamId, new IncrementEventWithId(streamId));
        await session2.SaveChangesAsync();

        var third = store.LightweightSession();
        var readModel2 = third.Load<CounterWithCreate>(streamId);

        readModel2.ShouldNotBeNull();
        readModel2.Id.ShouldBe(streamId);
        readModel2.CreateCounter.ShouldBe(1);
        // Fails as well: ApplyCounter is 1.
        readModel2.ApplyCounter.ShouldBe(3);
    }

    [Fact]
    public async Task projection_with_create_method_should_use_create_not_apply()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new IncrementEvent(), new IncrementEvent(), new IncrementEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<CounterWithCreate>(streamId);

        Assert.NotNull(aggregate);
        Assert.Equal(streamId, aggregate.Id);
        Assert.Equal(1, aggregate.CreateCounter);
        Assert.Equal(2, aggregate.ApplyCounter);
    }

    [Fact]
    public async Task projection_with_default_ctor_should_use_apply()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new IncrementEvent(), new IncrementEvent(), new IncrementEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<CounterWithDefaultCtor>(streamId);

        Assert.NotNull(aggregate);
        Assert.Equal(streamId, aggregate.Id);
        Assert.Equal(0, aggregate.CreateCounter);
        Assert.Equal(3, aggregate.ApplyCounter);
    }

    [Fact]
    public async Task projection_with_create_and_ctor_should_use_create_on_event_match()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new IncrementEvent(), new IncrementEvent(), new IncrementEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<CounterWithCreateAndDefaultCtor>(streamId);

        Assert.NotNull(aggregate);
        Assert.Equal(streamId, aggregate.Id);
        Assert.Equal(1, aggregate.CreateCounter);
        Assert.Equal(2, aggregate.ApplyCounter);
    }

    [Fact]
    public async Task projection_with_create_and_ctor_should_use_default_ctor_on_no_event_match()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new UnrelatedEvent(), new IncrementEvent(), new IncrementEvent(),
            new IncrementEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<CounterWithCreateAndDefaultCtor>(streamId);

        Assert.NotNull(aggregate);
        Assert.Equal(streamId, aggregate.Id);
        Assert.Equal(0, aggregate.CreateCounter);
        Assert.Equal(3, aggregate.ApplyCounter);
    }

    public record IncrementEventWithId(Guid Id);

    public class CounterWithDefaultCtorProjection: SingleStreamProjection<CounterWithCreate>
    {
        public static CounterWithCreate Create(IncrementEventWithId ev)
        {
            return new CounterWithCreate(ev.Id, 1, 0);
        }

        public CounterWithCreate Apply(IncrementEventWithId ev, CounterWithCreate current)
        {
            return current with { ApplyCounter = current.ApplyCounter };
        }
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
            return current with { ApplyCounter = current.ApplyCounter + 1 };
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

        public Guid Id { get; init; }
        public int CreateCounter { get; init; }
        public int ApplyCounter { get; init; }

        public CounterWithDefaultCtor Apply(IncrementEvent @event, CounterWithDefaultCtor current)
        {
            return current with { ApplyCounter = current.ApplyCounter + 1 };
        }
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
