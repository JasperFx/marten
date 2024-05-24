using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using Marten.Events.Projections;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.DocumentTrackingByIdentity;

public class EventProjection_follow_up_operations: DaemonContext
{
    [Fact]
    public async Task rebuild_with_follow_up_operations_should_work()
    {
        StoreOptions(x => x.Projections.Add<NestedEntityEventProjection>(ProjectionLifecycle.Inline,
            asyncOptions => asyncOptions.EnableDocumentTrackingByIdentity = true));

        var nestedEntity = new NestedEntity("etc");

        var guid = Guid.NewGuid();

        await using var session = theStore.IdentitySession();

        session.Events.StartStream(guid,
            new EntityPublished(guid,
                new Dictionary<Guid, NestedEntity>
                {
                    { Guid.NewGuid(), nestedEntity }, { Guid.NewGuid(), nestedEntity }
                }));
        session.Events.Append(Guid.NewGuid(), new SomeOtherEntityWithNestedIdentifierPublished(guid));

        await session.SaveChangesAsync();

        var agent = await StartDaemon();

        await agent.RebuildProjectionAsync(nameof(NestedEntity), CancellationToken.None);
    }

    public record EntityPublished(Guid Id, Dictionary<Guid, NestedEntity> Entities);

    public record NestedEntity(string SomeInformation);

    public record NestedEntityProjection(Guid Id, List<NestedEntity> Entity);

    public record SomeOtherEntityWithNestedIdentifierPublished(Guid Id);

    public class NestedEntityEventProjection: EventProjection
    {
        public NestedEntityEventProjection()
        {
            ProjectionName = nameof(NestedEntity);

            Project<EntityPublished>((@event, operations) =>
            {
                var entity = new NestedEntityProjection(@event.Id, @event.Entities.Select(x => x.Value).ToList());

                operations.Store(entity);
            });

            ProjectAsync<SomeOtherEntityWithNestedIdentifierPublished>(async (@event, operations) =>
            {
                var entity = await operations.LoadAsync<NestedEntityProjection>(@event.Id);

                Assert.NotNull(entity);
            });
        }
    }

    public EventProjection_follow_up_operations(ITestOutputHelper output): base(output)
    {
    }
}
