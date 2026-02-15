using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Xunit;
using System.Threading.Tasks;

namespace EventSourcingTests.Bugs;

public class single_property_async : BugIntegrationContext
{
    public single_property_async()
    {
        StoreOptions(o =>
        {
            o.Events.StreamIdentity = StreamIdentity.AsString;
            o.Projections.Add<SingleProjection>(ProjectionLifecycle.Async);
        }, true);
    }

    [Fact]
    public async Task start_and_append_events()
    {
        await using var session = theStore.LightweightSession();

        var stream = session.Events.StartStream<SingleProp>("key", new SinglePropCreate());

        await session.SaveChangesAsync();

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(15.Seconds());

        var aggregate = await theSession.LoadAsync<SingleProp>(stream.Key!);

        Assert.NotNull(aggregate);
    }
}

public class SingleProjection: SingleStreamProjection<SingleProp, string>
{
    public SingleProp Create(IEvent<SinglePropCreate> @event)
    {
        return new SingleProp(@event.StreamKey);
    }
}

public record SingleProp(string Id);

public record SinglePropCreate;
