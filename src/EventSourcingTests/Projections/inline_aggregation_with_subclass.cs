using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections;

public class inline_aggregation_with_subclass: OneOffConfigurationsContext
{
    public inline_aggregation_with_subclass()
    {
        StoreOptions(x =>
        {
            x.Schema.For<FooBase>().AddSubClass<FooA>();

            x.Projections.Snapshot<FooA>(ProjectionLifecycle.Inline);
        });
    }

    [Fact]
    public async Task can_create_subclass_projection()
    {
        var description = "FooDescription";

        var streamId = theSession.Events.StartStream(new FooACreated { Description = description }).Id;
        await theSession.SaveChangesAsync();

        var fooInstance = theSession.Query<FooA>().Single(x => x.Id == streamId);

        fooInstance.Id.ShouldBe(streamId);
        fooInstance.Description.ShouldBe(description);
    }

    [Fact]
    public async Task can_query_subclass_root()
    {
        var description = "FooDescription";

        var streamId = theSession.Events.StartStream(new FooACreated { Description = description }).Id;
        await theSession.SaveChangesAsync();

        var fooInstance = theSession.Query<FooBase>().Single(x => x.Id == streamId);

        fooInstance.Id.ShouldBe(streamId);
        fooInstance.ShouldBeOfType<FooA>();
    }
}

public abstract class FooBase
{
    public Guid Id { get; set; }
}

public class FooA: FooBase
{
    public string Description { get; set; }

    public void Apply(FooACreated @event)
    {
        Description = @event.Description;
    }
}

public class FooACreated
{
    public string Description { get; set; }
}
