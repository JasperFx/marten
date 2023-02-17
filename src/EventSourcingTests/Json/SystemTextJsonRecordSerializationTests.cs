using System;
using System.Threading.Tasks;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Linq;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Weasel.Core;
using Xunit;
using Shouldly;

namespace EventSourcingTests.Json;

public class SystemTextJsonRecordSerializationTests: IntegrationContext
{
    public SystemTextJsonRecordSerializationTests(DefaultStoreFixture fixture): base(fixture)
    {
    }

    [Fact]
    public async Task ForSystemTextJson_ProjectionShouldBeUpdated()
    {
        StoreOptions(opts =>
        {
            // Optionally configure the serializer directly
            opts.Serializer(new SystemTextJsonSerializer
            {
                // Optionally override the enum storage
                EnumStorage = EnumStorage.AsString,

                // Optionally override the member casing
                Casing = Casing.CamelCase,
            });

            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.MetadataConfig.EnableAll();
            opts.Schema.For<Resource>().DatabaseSchemaName("resources");

            opts.Projections.Add<ResourceProjection>(ProjectionLifecycle.Inline);

            opts.AutoCreateSchemaObjects = AutoCreate.All;

            opts.DatabaseSchemaName = "fleetmonitor";
            opts.Events.DatabaseSchemaName = "events";
        });

        var resourceId = Guid.NewGuid();
        var resourceName = "Test";

        theSession.Events.Append(resourceId, new ResourceCreatedEvent(resourceName));
        await theSession.SaveChangesAsync();

        var resource = await theSession.Query<Resource>().SingleOrDefaultAsync(r => r.Id == resourceId);

        resource.ShouldNotBeNull();
        resource.Id.ShouldBe(resourceId);
        resource.Name.ShouldBe(resourceName);
        resource.State.ShouldBe(ResourceState.Enabled);

        theSession.Events.Append(resourceId, new ResourceDisabledEvent());
        await theSession.SaveChangesAsync();

        resource = await theSession.Query<Resource>().SingleOrDefaultAsync(r => r.Id == resourceId);

        resource.ShouldNotBeNull();
        resource.Id.ShouldBe(resourceId);
        resource.Name.ShouldBe(resourceName);
        resource.State.ShouldBe(ResourceState.Disabled);
    }
}

public record Event;

public record ResourceCreatedEvent(string Name): Event;

public record ResourceRemovedEvent(): Event;

public record ResourceEnabledEvent(): Event;

public record ResourceDisabledEvent(): Event;

public class ResourceProjection: SingleStreamAggregation<Resource>
{
    public ResourceProjection()
    {
        DeleteEvent<ResourceRemovedEvent>();

        Lifecycle = ProjectionLifecycle.Inline;
    }

    public void Apply(ResourceDisabledEvent e, Resource resource) => resource.State = ResourceState.Disabled;

    public void Apply(ResourceEnabledEvent e, Resource resource)
    {
        resource.State = ResourceState.Enabled;
    }

    public Resource Create(ResourceCreatedEvent create)
    {
        return new Resource { Name = create.Name, State = ResourceState.Enabled };
    }
}

public record Resource
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public ResourceState State { get; set; }
}

public enum ResourceState
{
    Disabled,
    Enabled
}
