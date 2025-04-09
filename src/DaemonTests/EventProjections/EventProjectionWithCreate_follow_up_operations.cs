using System;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.EventProjections;

public class EventProjectionWithCreate_follow_up_operations: DaemonContext
{
    [Fact]
    public async Task rebuild_with_follow_up_operations_should_work()
    {
        StoreOptions(x => x.Projections.Add<EntityProjection>(ProjectionLifecycle.Inline,
            asyncOptions => asyncOptions.EnableDocumentTrackingByIdentity = true));

        var entityId = Guid.NewGuid();

        await using var session = theStore.IdentitySession();

        session.Events.StartStream(entityId, new EntityCreated(entityId, "Some name"));
        await session.SaveChangesAsync();
        session.Events.Append(entityId, new EntityNameUpdated(entityId, "New name"));
        await session.SaveChangesAsync();

        var agent = await StartDaemon();

        await agent.RebuildProjectionAsync(nameof(EntityProjection), CancellationToken.None);

        var shoppingCartRebuilt = await session.LoadAsync<Entity>(entityId);

        shoppingCartRebuilt!.Id.ShouldBe(entityId);
        shoppingCartRebuilt.Name.ShouldBe("New name");
    }


    [Fact]
    public async Task regular_usage_follow_up_operations_should_work()
    {
        StoreOptions(x => x.Projections.Add<EntityProjection>(ProjectionLifecycle.Async,
            asyncOptions => asyncOptions.EnableDocumentTrackingByIdentity = true));

        var entityId = Guid.NewGuid();

        await using var session = theStore.IdentitySession();

        session.Events.StartStream(entityId, new EntityCreated(entityId, "Some name"));
        await session.SaveChangesAsync();
        session.Events.Append(entityId, new EntityNameUpdated(entityId, "New name"));
        await session.SaveChangesAsync();

        var daemon = await StartDaemon();

        await daemon.Tracker.WaitForShardState($"{nameof(EntityProjection)}:All", 2);

        var entity = await session.LoadAsync<Entity>(entityId);

        entity.ShouldNotBeNull();

        entity.Id.ShouldBe(entityId);
        entity.Name.ShouldBe("New name");
    }

    public record Entity(Guid Id, string Name);

    public record EntityCreated(Guid Id, string Name);

    public record EntityNameUpdated(Guid Id, string Name);

    public class EntityProjection: EventProjection
    {
        public EntityProjection()
        {
            ProjectionName = nameof(EntityProjection);
        }

        public Entity Create(EntityCreated @event)
            => new(@event.Id, @event.Name);

        public async Task Project(EntityNameUpdated @event, IDocumentOperations operations,
            CancellationToken cancellationToken)
        {
            var stock = await operations.LoadAsync<Entity>(@event.Id, cancellationToken).ConfigureAwait(false);
            if (stock is null)
            {
                throw new ArgumentNullException(nameof(stock), "Stock does not exist!");
            }

            stock = stock with { Name = @event.Name };

            operations.Store(stock);
        }
    }

    public EventProjectionWithCreate_follow_up_operations(ITestOutputHelper output): base(output)
    {
    }
}
