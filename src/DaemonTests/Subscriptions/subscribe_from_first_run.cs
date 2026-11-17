using System;
using System.Linq;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Subscriptions;

/// <summary>
/// #5740 / jasperfx#809: <c>AsyncOptions.SubscribeFromFirstRun()</c> end to end on Marten. It needs the
/// persisted floor seam, so before #5740 it failed every shard start with a NotSupportedException.
/// </summary>
[Collection("subscriptions")]
public class subscribe_from_first_run: HostedStoreContext
{
    private readonly ITestOutputHelper _output;
    private readonly FakeSubscription theSubscription = new();
    private IHost _host;

    public subscribe_from_first_run(ITestOutputHelper output)
    {
        _output = output;
    }

    public override async ValueTask InitializeAsync()
    {
        await SchemaUtils.DropSchema(ConnectionSource.ConnectionString, SchemaName);

        _host = await StartHostAsync(opts =>
        {
            opts.DotNetLogger = new TestLogger<FakeSubscription>(_output);

            theSubscription.Options.SubscribeFromFirstRun();
            opts.Events.Subscribe(theSubscription);
        });

        await Store.Advanced.Clean.DeleteAllEventDataAsync();
    }

    private IDocumentStore Store => _host.Services.GetRequiredService<IDocumentStore>();

    private IEventDatabase Database => (IEventDatabase)Store.Storage.Database;

    [Fact]
    public async Task the_first_run_pins_the_floor_and_skips_the_history_below_it()
    {
        await pumpInEvents();

        using (var daemon = await Store.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();

            await pumpInEvents();
            await Store.WaitForNonStaleProjectionDataAsync(20.Seconds());
        }

        (await Database.FetchProjectionFloorAsync(theSubscription.Name, null)).ShouldBe(16);

        theSubscription.EventsEncountered.Count.ShouldBe(16);
        theSubscription.EventsEncountered.ShouldAllBe(x => x.Sequence > 16);
    }

    [Fact]
    public async Task the_stored_floor_still_holds_once_the_progress_is_gone()
    {
        // The floor has to survive what deletes the progression row (a rebuild's teardown, an operator's
        // reset), or the next start would treat the projection as new and walk the whole history.
        await pumpInEvents();

        using (var daemon = await Store.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await pumpInEvents();
            await Store.WaitForNonStaleProjectionDataAsync(20.Seconds());
            await daemon.StopAllAsync();
        }

        await Database.DeleteProjectionProgressByShardNameAsync($"{theSubscription.Name}:All");
        theSubscription.EventsEncountered.Clear();

        using (var daemon = await Store.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await Store.WaitForNonStaleProjectionDataAsync(20.Seconds());
        }

        theSubscription.EventsEncountered.ShouldNotBeEmpty();
        theSubscription.EventsEncountered.Min(x => x.Sequence).ShouldBe(17);
    }

    private async Task pumpInEvents()
    {
        await using var theSession = Store.LightweightSession();

        var events1 = new object[] { new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events2 = new object[] { new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events3 = new object[] { new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events4 = new object[] { new EventSourcingTests.Aggregation.EEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };

        theSession.Events.StartStream(Guid.NewGuid(), events1);
        theSession.Events.StartStream(Guid.NewGuid(), events2);
        theSession.Events.StartStream(Guid.NewGuid(), events3);
        theSession.Events.StartStream(Guid.NewGuid(), events4);

        await theSession.SaveChangesAsync();
    }
}
