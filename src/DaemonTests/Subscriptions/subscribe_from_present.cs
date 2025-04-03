using System;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Subscriptions;

[Collection("subscriptions")]
public class subscribe_from_present : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly FakeSubscription theSubscription = new();
    private readonly FakeTimeProvider theProvider = new();
    private IHost _host;

    public subscribe_from_present(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await SchemaUtils.DropSchema(ConnectionSource.ConnectionString, "subscriptions_start");

        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "subscriptions_start";

                    opts.DotNetLogger = new TestLogger<FakeSubscription>(_output);
                    opts.DisableNpgsqlLogging = true;

                    theSubscription.Options.SubscribeFromPresent();
                    opts.Events.Subscribe(theSubscription);

                    opts.Events.TimeProvider = theProvider;
                });
            }).StartAsync();

        var store = _host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.DeleteAllEventDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task start_from_scratch()
    {
        var theStore = _host.Services.GetRequiredService<IDocumentStore>();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await pumpInEvents();

        await theStore.WaitForNonStaleProjectionDataAsync(20.Seconds());

        theSubscription.EventsEncountered.Count.ShouldBe(16);
    }

    [Fact]
    public async Task can_successfully_start_and_function()
    {
        var theStore = _host.Services.GetRequiredService<IDocumentStore>();

        theProvider.SetUtcNow(DateTimeOffset.UtcNow.Subtract(1.Hours()));
        await pumpInEvents();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        theProvider.Advance(1.Hours());

        await pumpInEvents();

        await theStore.WaitForNonStaleProjectionDataAsync(20.Seconds());

        theSubscription.EventsEncountered.Count.ShouldBe(16);
    }

    private async Task pumpInEvents()
    {
        var theStore = _host.Services.GetRequiredService<IDocumentStore>();
        await using var theSession = theStore.LightweightSession();

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
