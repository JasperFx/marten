using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_2666_create_or_apply_but_not_both_should_apply : BugIntegrationContext
{
    [Fact]
    public async Task projection_with_create_method_should_use_create_not_apply()
    {
        StoreOptions(_ =>
        {
            _.Projections.Snapshot<CounterWithCreate>(ProjectionLifecycle.Async);
        });
        using var theAsyncDaemon = await theStore.BuildProjectionDaemonAsync();
        await theAsyncDaemon.StartAllAsync();

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new IncrementEvent(), new IncrementEvent(), new IncrementEvent());
        await theSession.SaveChangesAsync();
        await theAsyncDaemon.WaitForNonStaleData(5.Seconds());

        var aggregate = await theSession.Query<CounterWithCreate>().FirstOrDefaultAsync(x => x.Id == streamId).ConfigureAwait(false);

        Assert.NotNull(aggregate);
        Assert.Equal(streamId, aggregate.Id);
        Assert.Equal(1, aggregate.CreateCounter);
        Assert.Equal(2, aggregate.ApplyCounter);
    }

    [Fact]
    public async Task projection_with_default_ctor_should_use_apply()
    {
        StoreOptions(_ =>
        {
            _.Projections.Snapshot<CounterWithDefaultCtor>(ProjectionLifecycle.Async);
        });
        using var theAsyncDaemon = await theStore.BuildProjectionDaemonAsync();
        await theAsyncDaemon.StartAllAsync();

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new IncrementEvent(), new IncrementEvent(), new IncrementEvent());
        await theSession.SaveChangesAsync();
        await theAsyncDaemon.WaitForNonStaleData(5.Seconds());

        var aggregate = await theSession.Query<CounterWithDefaultCtor>().FirstOrDefaultAsync(x => x.Id == streamId);

        Assert.NotNull(aggregate);
        Assert.Equal(streamId, aggregate.Id);
        Assert.Equal(0, aggregate.CreateCounter);
        Assert.Equal(3, aggregate.ApplyCounter);
    }

    [Fact]
    public async Task projection_with_create_and_ctor_should_use_create_on_event_match()
    {
        StoreOptions(_ =>
        {
            _.Projections.Snapshot<CounterWithCreateAndDefaultCtor>(ProjectionLifecycle.Async);
        });
        using var theAsyncDaemon = await theStore.BuildProjectionDaemonAsync();
        await theAsyncDaemon.StartAllAsync();

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new IncrementEvent(), new IncrementEvent(), new IncrementEvent());
        await theSession.SaveChangesAsync();
        await theAsyncDaemon.WaitForNonStaleData(5.Seconds());

        var aggregate = await theSession.Query<CounterWithCreateAndDefaultCtor>().Where(x => x.Id == streamId).FirstOrDefaultAsync();

        Assert.NotNull(aggregate);
        Assert.Equal(streamId, aggregate.Id);
        Assert.Equal(1, aggregate.CreateCounter);
        Assert.Equal(2, aggregate.ApplyCounter);
    }

    [Fact]
    public async Task projection_with_create_and_ctor_should_use_default_ctor_on_no_event_match()
    {
        StoreOptions(_ =>
        {
            _.Projections.Snapshot<CounterWithCreateAndDefaultCtor>(ProjectionLifecycle.Async);
        });
        using var theAsyncDaemon = await theStore.BuildProjectionDaemonAsync();
        await theAsyncDaemon.StartAllAsync();

        var streamId = Guid.NewGuid();

        // The projection doesn't do anything unless impacted by an event it cares about,
        // so I added an empty handler for UnrelatedEvent
        theSession.Events.Append(streamId, new UnrelatedEvent(), new IncrementEvent(), new IncrementEvent(), new IncrementEvent());
        await theSession.SaveChangesAsync();
        await theAsyncDaemon.WaitForNonStaleData(5.Seconds());

        var aggregate = await theSession.Query<CounterWithCreateAndDefaultCtor>().FirstOrDefaultAsync(x => x.Id == streamId);

        Assert.NotNull(aggregate);
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

        public CounterWithCreateAndDefaultCtor Apply(UnrelatedEvent e, CounterWithCreateAndDefaultCtor current)
        {
            return current;
        }

        public CounterWithCreateAndDefaultCtor Apply(IncrementEvent @event, CounterWithCreateAndDefaultCtor current)
        {
            return current with { ApplyCounter = current.ApplyCounter + 1 };
        }
    }
}

