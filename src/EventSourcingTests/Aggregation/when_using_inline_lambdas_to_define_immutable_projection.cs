using System;
using System.Threading.Tasks;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class when_using_inline_lambdas_to_define_immutable_projection : OneOffConfigurationsContext
{
    [Fact]
    public async Task async_apply_with_immutable_aggregate()
    {
        StoreOptions(_ => _.Projections.Add<MyAggregateImmutableProjection>(ProjectionLifecycle.Live));

        var user1 = new User {UserName = "Creator"};
        var user2 = new User {UserName = "Updater"};

        TheSession.Store(user1, user2);
        await TheSession.SaveChangesAsync();

        var streamId = Guid.NewGuid();
        TheSession.Events.Append(streamId, new UserStartedRecord(streamId, user1.Id), new UserUpdatedRecord(streamId, user2.Id));
        await TheSession.SaveChangesAsync();

        var aggregate = await TheSession.Events.AggregateStreamAsync<MyAggregateRecord>(streamId);

        aggregate.Created.ShouldBe(user1.UserName);
        aggregate.UpdatedBy.ShouldBe(user2.UserName);
    }

    public record UserStartedRecord(Guid Id, Guid UserId);

    public record UserUpdatedRecord(Guid Id, Guid UserId);

    public record MyAggregateRecord(Guid Id, string Created, string UpdatedBy);

    public class MyAggregateImmutableProjection: SingleStreamProjection<MyAggregateRecord>
    {
        public MyAggregateImmutableProjection()
        {
            CreateEvent<UserStartedRecord>(async (@event, session) =>
            {
                var user = await session.LoadAsync<User>(@event.UserId);
                return new MyAggregateRecord(@event.UserId, user.UserName, null);
            });

            ProjectEventAsync<UserUpdatedRecord>(async (session, a, @event) =>
            {
                var user = await session.LoadAsync<User>(@event.UserId);
                return a with { UpdatedBy = user.UserName };
            });
        }
    }
}
