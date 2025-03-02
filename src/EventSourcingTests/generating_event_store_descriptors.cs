using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Examples;
using EventSourcingTests.Examples.TeleHealth;
using EventSourcingTests.FetchForWriting;
using EventSourcingTests.Projections;
using JasperFx.Core.Descriptions;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class generating_event_store_descriptors
{
    [Fact]
    public async Task find_default_capability_with_events_and_projections()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "capabilities";

                    opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
                    opts.Projections.Add<LapMultiStreamProjection>(ProjectionLifecycle.Inline);
                    opts.Projections.LiveStreamAggregation<QuestParty>();
                    opts.Projections.Add<MyAggregateProjection>(ProjectionLifecycle.Inline);
                });
            }).StartAsync();

        var capabilities = host.Services.GetServices<IEventStoreCapability>().ToArray();
        capabilities.Length.ShouldBe(1);

        var usage = await capabilities[0].TryCreateUsage(CancellationToken.None);

        usage.ShouldNotBeNull();

        usage.Subscriptions.Count.ShouldBe(4);
        usage.Events.Any().ShouldBeTrue();
    }
}
