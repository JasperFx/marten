using System;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.Projections;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using UserCreated = DaemonTests.Aggregations.UserCreated;

namespace DaemonTests.Bugs;

public class Bug_2159_using_QuerySession_within_async_aggregation : BugIntegrationContext
{
    [Fact]
    public async Task use_query_session_within_async_aggregation()
    {
        StoreOptions(opts => opts.Projections.Add(new UserAggregate(), ProjectionLifecycle.Async));

        var streamId = Guid.NewGuid();
        var user = new User { UserName = "Blue"};

        theSession.Store(user);
        await theSession.SaveChangesAsync();

        theSession.Events.StartStream(streamId, new UserCreated());
        await theSession.SaveChangesAsync();


        theSession.Events.Append(streamId, new UserUpdated{UserId = user.Id});
        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<UserAggregate>(CancellationToken.None);

        var aggregate = await theSession.LoadAsync<MyAggregate>(streamId);
        aggregate.UpdatedBy.ShouldBe("Blue");

    }
}

public class UserAggregate: SingleStreamProjection<MyAggregate, Guid>
{
    public UserAggregate()
    {
        DeleteEvent<UserDeleted>();
    }


    public async Task Apply(UserUpdated @event, MyAggregate aggregate, IQuerySession session)
    {
        var user = await session.LoadAsync<User>(@event.UserId);
        aggregate.UpdatedBy = user.UserName;
    }

    public MyAggregate Create(UserCreated @event)
    {
        return new MyAggregate { };
    }


}
