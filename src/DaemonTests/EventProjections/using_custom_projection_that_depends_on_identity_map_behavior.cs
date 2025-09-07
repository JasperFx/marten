using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.EventProjections;

public class using_custom_projection_that_depends_on_identity_map_behavior: DaemonContext
{
    [Fact]
    public async Task rebuild_with_follow_up_operations_should_work()
    {
        StoreOptions(x => x.Projections.Add(
            new NestedEntityEventProjection(),
            ProjectionLifecycle.Inline,
            nameof(NestedEntity),
            asyncOptions => asyncOptions.EnableDocumentTrackingByIdentity = true
        ));

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

    public class NestedEntityEventProjection: IProjection
    {
        private readonly Type[] _handledEventTypes =
        {
            typeof(EntityPublished), typeof(SomeOtherEntityWithNestedIdentifierPublished)
        };

        public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events, CancellationToken cancellation)
        {
            // TODO -- add an extension method helper to select the right event types? Or show an Include
            var eventsToApply = events
                .Where(@event => _handledEventTypes.Contains(@event.EventType))
                .Select(@event => @event.Data).ToArray();

            foreach (var @event in eventsToApply)
            {
                switch (@event)
                {
                    case EntityPublished entityPublished:
                        var entity = new NestedEntityProjection(
                            entityPublished.Id,
                            entityPublished.Entities.Select(x => x.Value).ToList()
                        );

                        operations.Store(entity);
                        break;
                    case SomeOtherEntityWithNestedIdentifierPublished someOtherEntityWithNestedIdentifierPublished:
                        var someOtherEntity = await operations.LoadAsync<NestedEntityProjection>(
                            someOtherEntityWithNestedIdentifierPublished.Id, cancellation
                        );

                        Assert.NotNull(someOtherEntity);
                        break;
                }
            }
        }

    }

    public using_custom_projection_that_depends_on_identity_map_behavior(ITestOutputHelper output): base(output)
    {
    }
}
