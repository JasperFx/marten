using System;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.FetchForWriting;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Aggregation;

public class when_doing_live_aggregations : AggregationContext
{
    private readonly ITestOutputHelper _output;

    public when_doing_live_aggregations(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }

    [Fact]
    public async Task sync_apply_and_default_create()
    {
        UsingDefinition<AllSync>();
        var aggregate = await LiveAggregation(x =>
        {
            x.B();
            x.C();
            x.B();
            x.C();
            x.C();
            x.A();
            x.D();
        });



        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(2);
        aggregate.CCount.ShouldBe(3);
        aggregate.DCount.ShouldBe(1);
    }

    [Fact]
    public async Task when_requesting_an_aggregate_for_an_invalid_version()
    {
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new CEvent(),
            new DEvent());
        await theSession.SaveChangesAsync();

        var aggregate1 = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        var aggregateAt4 = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId, version:4);
        var aggregateAt5 = await theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId, version: 5);

        aggregateAt4.ShouldBe(aggregate1);

        aggregateAt5.ShouldBeNull();
    }

    [Fact]
    public async Task when_requesting_an_aggregate_for_an_invalid_version_with_string_identifiers()
    {
        UseStreamIdentity(StreamIdentity.AsString);
        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent(), new CEvent(),
            new DEvent());
        await theSession.SaveChangesAsync();

        var aggregate1 = await theSession.Events.AggregateStreamAsync<SimpleAggregateAsString>(streamId);
        var aggregateAt4 = await theSession.Events.AggregateStreamAsync<SimpleAggregateAsString>(streamId, version:4);
        var aggregateAt5 = await theSession.Events.AggregateStreamAsync<SimpleAggregateAsString>(streamId, version: 5);

        aggregateAt4.ShouldBe(aggregate1);

        aggregateAt5.ShouldBeNull();
    }

    [Fact]
    public async Task sync_apply_and_specific_create()
    {
        UsingDefinition<AllSync>();
        var aggregate = await LiveAggregation(x =>
        {
            x.Add(new CreateEvent(2, 3, 4, 5));

            x.B();
            x.C();
            x.B();
            x.C();
            x.C();
            x.A();
            x.D();
        });

        aggregate.ACount.ShouldBe(3);
        aggregate.BCount.ShouldBe(5);
        aggregate.CCount.ShouldBe(7);
        aggregate.DCount.ShouldBe(6);
    }

    [Fact]
    public async Task async_create_and_apply_with_session()
    {
        var user1 = new User {UserName = "Creator"};
        var user2 = new User {UserName = "Updater"};

        theSession.Store(user1, user2);
        await theSession.SaveChangesAsync();

        UsingDefinition<AsyncEverything>();

        var aggregate = await LiveAggregation(x =>
        {
            x.Add(new UserStarted {UserId = user1.Id});
            x.Add(new UserUpdated {UserId = user2.Id});
        });

        aggregate.Created.ShouldBe(user1.UserName);
        aggregate.UpdatedBy.ShouldBe(user2.UserName);
    }

    [Fact]
    public async Task using_sync_value_task_with_otherwise_async_create()
    {
        var user1 = new User {UserName = "Creator"};
        var user2 = new User {UserName = "Updater"};

        theSession.Store(user1, user2);
        await theSession.SaveChangesAsync();

        UsingDefinition<AsyncEverything>();

        var aggregate = await LiveAggregation(x =>
        {
            x.A();
            x.A();
            x.A();
        });

        aggregate.ACount.ShouldBe(3);
    }

    [Fact]
    public async Task async_create_and_sync_apply()
    {
        var user1 = new User {UserName = "Creator"};

        theSession.Store(user1);
        await theSession.SaveChangesAsync();

        UsingDefinition<AsyncCreateSyncApply>();

        var aggregate = await LiveAggregation(x =>
        {
            x.Add(new UserStarted {UserId = user1.Id});
            x.B();
            x.C();
            x.B();
            x.C();
            x.C();
            x.A();
            x.D();
        });

        aggregate.Created.ShouldBe(user1.UserName);
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(2);
        aggregate.CCount.ShouldBe(3);
        aggregate.DCount.ShouldBe(1);
    }

    [Fact]
    public async Task sync_create_and_async_apply()
    {
        var user1 = new User {UserName = "Updater"};

        theSession.Store(user1);
        await theSession.SaveChangesAsync();

        UsingDefinition<SyncCreateAsyncApply>();

        var aggregate = await LiveAggregation(x =>
        {
            x.Add(new CreateEvent(2, 3, 4, 5));
            x.Add(new UserUpdated {UserId = user1.Id});
            x.B();
            x.C();
            x.B();
            x.C();
            x.C();
            x.A();
            x.D();
        });

        aggregate.UpdatedBy.ShouldBe(user1.UserName);
        aggregate.ACount.ShouldBe(3);
        aggregate.BCount.ShouldBe(5);
        aggregate.CCount.ShouldBe(7);
        aggregate.DCount.ShouldBe(6);
    }

    [Fact]
    public async Task using_event_metadata()
    {
        UsingDefinition<UsingMetadata>();

        var streamId = Guid.NewGuid();
        var aId = Guid.NewGuid();

        var aggregate = await LiveAggregation(x =>
        {
            x.Add(new CreateEvent(2, 3, 4, 5)).StreamId = streamId;
            x.A().Id = aId;
        });

        aggregate.Id.ShouldBe(streamId);
        aggregate.EventId.ShouldBe(aId);
    }


}





public class UserStarted
{
    public Guid UserId { get; set; }
}

public class UserUpdated
{
    public Guid UserId { get; set; }
}

public class UsingMetadata : SingleStreamProjection<MyAggregate, Guid>
{
    public MyAggregate Create(CreateEvent create, IEvent e)
    {
        return new MyAggregate
        {
            ACount = create.A,
            BCount = create.B,
            CCount = create.C,
            DCount = create.D,
            Id = e.StreamId
        };
    }

    public void Apply(IEvent<AEvent> @event, MyAggregate aggregate)
    {
        aggregate.EventId = @event.Id;
        aggregate.ACount++;
    }
}

public class AsyncEverything: SingleStreamProjection<MyAggregate, Guid>
{
    public async Task<MyAggregate> Create(UserStarted @event, IQuerySession session, CancellationToken cancellation)
    {
        var user = await session.LoadAsync<User>(@event.UserId, cancellation);
        return new MyAggregate
        {
            Created = user.UserName
        };
    }

    public async Task Apply(UserUpdated @event, MyAggregate aggregate, IQuerySession session)
    {
        var user = await session.LoadAsync<User>(@event.UserId);
        aggregate.UpdatedBy = user.UserName;
    }

    public void Apply(AEvent @event, MyAggregate aggregate)
    {
        aggregate.ACount++;
    }


}

public class AsyncCreateSyncApply: SingleStreamProjection<MyAggregate, Guid>
{
    public async Task<MyAggregate> Create(UserStarted @event, IQuerySession session, CancellationToken cancellation)
    {
        var user = await session.LoadAsync<User>(@event.UserId, cancellation);
        return new MyAggregate
        {
            Created = user.UserName
        };
    }

    public void Apply(AEvent @event, MyAggregate aggregate)
    {
        aggregate.ACount++;
    }

    public void Apply(BEvent @event, MyAggregate aggregate)
    {
        aggregate.BCount++;
    }

    public void Apply(MyAggregate aggregate, CEvent @event)
    {
        aggregate.CCount++;
    }

    public void Apply(MyAggregate aggregate, DEvent @event)
    {
        aggregate.DCount++;
    }

}

public class SyncCreateAsyncApply: SingleStreamProjection<MyAggregate, Guid>
{
    public MyAggregate Create(CreateEvent @event)
    {
        return new MyAggregate
        {
            ACount = @event.A,
            BCount = @event.B,
            CCount = @event.C,
            DCount = @event.D
        };
    }

    public async Task Apply(UserUpdated @event, MyAggregate aggregate, IQuerySession session)
    {
        var user = await session.LoadAsync<User>(@event.UserId);
        aggregate.UpdatedBy = user.UserName;
    }

    public void Apply(AEvent @event, MyAggregate aggregate)
    {
        aggregate.ACount++;
    }

    public void Apply(BEvent @event, MyAggregate aggregate)
    {
        aggregate.BCount++;
    }

    public void Apply(MyAggregate aggregate, CEvent @event)
    {
        aggregate.CCount++;
    }

    public void Apply(MyAggregate aggregate, DEvent @event)
    {
        aggregate.DCount++;
    }
}


public class AllSync: SingleStreamProjection<MyAggregate, Guid>
{
    public AllSync()
    {
        Name = "AllSync";
    }

    public MyAggregate Create(CreateEvent @event)
    {
        return new MyAggregate
        {
            ACount = @event.A,
            BCount = @event.B,
            CCount = @event.C,
            DCount = @event.D
        };
    }

    public void Apply(AEvent @event, MyAggregate aggregate)
    {
        aggregate.ACount++;
    }

    public MyAggregate Apply(BEvent @event, MyAggregate aggregate)
    {
        return new MyAggregate
        {
            ACount = aggregate.ACount,
            BCount = aggregate.BCount + 1,
            CCount = aggregate.CCount,
            DCount = aggregate.DCount,
            Id = aggregate.Id
        };
    }

    public void Apply(MyAggregate aggregate, CEvent @event)
    {
        aggregate.CCount++;
    }

    public MyAggregate Apply(MyAggregate aggregate, DEvent @event)
    {
        return new MyAggregate
        {
            ACount = aggregate.ACount,
            BCount = aggregate.BCount,
            CCount = aggregate.CCount,
            DCount = aggregate.DCount + 1,
            Id = aggregate.Id
        };
    }
}
