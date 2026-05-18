using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Metadata;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3946_tenancy_with_for_tenant_and_projection_issues : BugIntegrationContext
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task when_projection_is_multi_stream_then_tenant_is_passed_to_projection_document(bool useRebuild)
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AddEventType<EntityCreated>();
            opts.Events.AddEventType<AggregateCreated>();

            opts.Projections.Add(new EntityProjection(), ProjectionLifecycle.Inline);
            opts.Projections.Add(new AggregateProjection(), ProjectionLifecycle.Inline);
        });

        var aggregateId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var tenant = "Foo";

        await using var session = theStore.LightweightSession();
        session.ForTenant(tenant).Events.StartStream<Aggregate>(aggregateId, [new AggregateCreated(aggregateId), new EntityCreated(entityId, aggregateId)]);
        await session.SaveChangesAsync();

        var entities = await session.Query<Entity>().Where(x => x.AnyTenant()).ToListAsync();

        if (useRebuild)
        {
            var agent = await theStore.BuildProjectionDaemonAsync();
            await agent.RebuildProjectionAsync<Entity>(CancellationToken.None);
        }

        await using var secondSession = theStore.LightweightSession(tenantId: tenant);
        var aggregate = await secondSession.LoadAsync<Aggregate>(aggregateId);
        var entity = await secondSession.LoadAsync<Entity>(entityId);

        Assert.NotNull(aggregate);
        Assert.Single(aggregate.Entities);
        Assert.NotNull(entity);  // Fails because tenant is not set on entity document without a rebuild
        Assert.Equal(entity.TenantId, tenant);
    }

    [DocumentAlias("aggregate")]
    public class Aggregate: ITenanted
    {
        public Guid Id { get; set; }
        public string? TenantId { get; set; }
        public List<Entity> Entities { get; set; } = [];
    }

    [DocumentAlias("entity")]
    public class Entity : ITenanted
    {
        public Guid Id { get; set; }
        public string? TenantId { get; set; }
    }

    public record AggregateCreated(Guid Id);
    public record EntityCreated(Guid Id, Guid AggregateId);

    public class AggregateProjection : SingleStreamProjection<Aggregate, Guid>
    {
        public static Aggregate Create(IEvent<AggregateCreated> @event) => new Aggregate
        {
            Id = @event.Data.Id,
            TenantId = @event.TenantId
        };

        public static void Apply(IEvent<EntityCreated> @event, Aggregate aggregate)
        {
            aggregate.Entities.Add(new Entity { Id = @event.Data.Id });
        }
    }

    public class EntityProjection : MultiStreamProjection<Entity, Guid>
    {
        public EntityProjection()
        {
            Identity<EntityCreated>(x => x.Id);
        }

        public static Entity Create(IEvent<EntityCreated> @event) => new Entity
        {
            Id = @event.Data.Id,
            TenantId = @event.TenantId
        };
    }
}



