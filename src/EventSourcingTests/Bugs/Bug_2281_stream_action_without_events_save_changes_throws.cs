using System;
using System.Threading.Tasks;

using Bug2281;

using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;

using Shouldly;

using Xunit;

namespace EventSourcingTests.Bugs
{
    public class Bug_2281_stream_action_without_events_save_changes_throws: BugIntegrationContext
    {
        [Fact]
        public async Task should_be_able_to_save_changes_when_stream_action_does_not_have_any_events()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.Projections.SelfAggregate<TestEntity>(ProjectionLifecycle.Inline);
            });

            var entityOneId = await CreateEntityForTest(documentStore, "Entity one", 0);
            var entityTwoId = await CreateEntityForTest(documentStore, "Entity two", 2);

            await using (var session = documentStore.OpenSession())
            {
                var entityOneStream = await session.Events.FetchForWriting<TestEntity>(entityOneId);
                var entityTwoStream = await session.Events.FetchForWriting<TestEntity>(entityTwoId);

                ChangeStatusIfZero(entityOneStream);
                ChangeStatusIfZero(entityTwoStream);

                await session.SaveChangesAsync();
            }

            await using (var session = documentStore.OpenSession())
            {
                var entityOne = session.Load<TestEntity>(entityOneId);
                entityOne.Status.ShouldBe(1);

                var entityTwo = session.Load<TestEntity>(entityTwoId);
                entityTwo.Status.ShouldBe(2);
            }
        }

        [Fact]
        public async Task should_be_able_to_save_changes_when_no_stream_action_has_any_events()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.Projections.SelfAggregate<TestEntity>(ProjectionLifecycle.Inline);
            });

            var entityOneId = await CreateEntityForTest(documentStore, "Entity one", 2);
            var entityTwoId = await CreateEntityForTest(documentStore, "Entity two", 2);

            await using (var session = documentStore.OpenSession())
            {
                var entityOneStream = await session.Events.FetchForWriting<TestEntity>(entityOneId);
                var entityTwoStream = await session.Events.FetchForWriting<TestEntity>(entityTwoId);

                ChangeStatusIfZero(entityOneStream);
                ChangeStatusIfZero(entityTwoStream);

                await session.SaveChangesAsync();
            }

            await using (var session = documentStore.OpenSession())
            {
                var entityOne = session.Load<TestEntity>(entityOneId);
                entityOne.Status.ShouldBe(2);

                var entityTwo = session.Load<TestEntity>(entityTwoId);
                entityTwo.Status.ShouldBe(2);
            }
        }

        private static async Task<Guid> CreateEntityForTest(IDocumentStore documentStore, string name, int status)
        {
            await using var session = await documentStore.LightweightSessionAsync();
            var stream = session.Events.StartStream<TestEntity>(new CreateEntityEvent
            {
                Name = name,
                Status = status
            });

            await session.SaveChangesAsync();
            return stream.Id;
        }

        private static void ChangeStatusIfZero(IEventStream<TestEntity> stream)
        {
            if (stream.Aggregate.Status == 0)
            {
                stream.AppendOne(new ChangeStatus { NewStatus = 1 });
            }
        }
    }
}

namespace Bug2281
{
    public class TestEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
        public int Status { get; set; }

        public void Apply(CreateEntityEvent createEvent)
        {
            Name = createEvent.Name;
            Status = createEvent.Status;
        }

        public void Apply(ChangeStatus changeStatus)
        {
            Status = changeStatus.NewStatus;
        }
    }

    public class CreateEntityEvent
    {
        public string Name { get; set; }
        public int Status { get; set; }
    }

    public class ChangeStatus
    {
        public int NewStatus { get; set; }
    }
}
