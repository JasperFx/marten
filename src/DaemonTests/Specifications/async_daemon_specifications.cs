using System;
using System.Threading.Tasks;
using Bobcat;
using Bobcat.CritterStack;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DaemonTests.Specifications;

/// <summary>
/// The async daemon specified through Bobcat's store-only vocabulary, against an application that runs the daemon
/// itself (<c>AddAsyncDaemon(DaemonMode.Solo)</c>). Nothing here waits by hand: every read of a projected document
/// opens with Bobcat's own catch-up step, which on a timeout names the shards that were behind
/// (<see cref="ProjectionCatchUpTimeoutException" />; see <see cref="catch_up_timeout_diagnostics" />).
/// </summary>
[BobcatFeature("The async daemon keeps read models current")]
public class async_daemon_specifications: CritterStackSpec, IClassFixture<ShipmentsHost>
{
    public async_daemon_specifications(ShipmentsHost app): base(app.Host)
    {
        ProjectionTimeout = 30.Seconds();
    }

    [Fact]
    public async Task an_async_projection_catches_up_with_an_appended_event()
    {
        var id = Guid.NewGuid();
        await GivenEvents<ShipmentSummary>(id, new ShipmentPacked(id, 3));

        await WhenActing("the shipment is shipped", services => append(services, id, new ShipmentShipped(id)));

        ThenEvents(new ShipmentShipped(id));
        await ThenReadModel<ShipmentSummary>(id, Specify<ShipmentSummary>()
            .With(x => x.Items, 3)
            .With(x => x.Shipped, true));
    }

    [Fact]
    public async Task an_async_projection_deletes_the_read_model_its_stream_cancels()
    {
        var id = Guid.NewGuid();
        await GivenEvents<ShipmentSummary>(id, new ShipmentPacked(id, 2));

        await WhenActing("the shipment is cancelled", services => append(services, id, new ShipmentCancelled(id)));

        ThenEvents(new ShipmentCancelled(id));
        await ThenNoReadModel<ShipmentSummary>(id);
    }

    private static async Task append(IServiceProvider services, Guid id, object @event)
    {
        await using var session = services.GetRequiredService<IDocumentStore>().LightweightSession();
        session.Events.Append(id, @event);
        await session.SaveChangesAsync();
    }
}

/// <summary>
/// A rebuild through the application's own running daemon, on a host of its own. JasperFx's rebuild stops the
/// shard's continuous agent, rebuilds with dedicated agents and disposes them, and does not start the continuous
/// agent again, so after a rebuild the shard sits at the rebuilt position and processes no new event. Sharing a host
/// with <see cref="async_daemon_specifications" />, whichever specification ran after this one timed out with
/// "'ShipmentSummary:All' at 2 of 4". Whether a hosted daemon should resume the shard is a question for Marten, not
/// for this specification.
/// </summary>
[BobcatFeature("The async daemon keeps read models current")]
public class rebuilding_through_the_running_daemon: CritterStackSpec, IClassFixture<ShipmentsRebuildHost>
{
    public rebuilding_through_the_running_daemon(ShipmentsRebuildHost app): base(app.Host)
    {
        ProjectionTimeout = 30.Seconds();
    }

    [Fact]
    public async Task a_rebuild_through_the_running_daemon_rebuilds_the_read_model()
    {
        var id = Guid.NewGuid();
        await GivenEvents<ShipmentSummary>(id, new ShipmentPacked(id, 5), new ShipmentShipped(id));

        await WhenActing("the ShipmentSummary projection is rebuilt",
            _ => Host.Daemon().RebuildProjectionAsync<ShipmentSummary>(TestContext.Current.CancellationToken));

        ThenNoEvents();
        await ThenReadModel<ShipmentSummary>(id, Specify<ShipmentSummary>()
            .With(x => x.Items, 5)
            .With(x => x.Shipped, true));
    }
}
