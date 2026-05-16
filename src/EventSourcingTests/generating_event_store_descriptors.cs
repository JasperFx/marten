using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.FetchForWriting;
using EventSourcingTests.Projections;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Storage;
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

        var capabilities = host.Services.GetServices<IEventStore>().ToArray();
        capabilities.Length.ShouldBe(1);

        var usage = await capabilities[0].TryCreateUsage(CancellationToken.None);

        usage.ShouldNotBeNull();

        usage.Subscriptions.Count.ShouldBe(4);
        usage.Events.Any().ShouldBeTrue();
    }

    [Fact]
    public async Task max_event_sequence_matches_highest_persisted_seq_id()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "max_seq_usage";
                });
            }).StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(Guid.NewGuid(), new QuestStarted { Name = "trip-1" }, new MembersJoined(1, "moria", "Frodo"));
            session.Events.StartStream(Guid.NewGuid(), new QuestStarted { Name = "trip-2" }, new MembersJoined(1, "shire", "Sam"));
            await session.SaveChangesAsync();
        }

        var database = (MartenDatabase)store.Storage.Database;
        var expectedMax = await database.FetchMaxEventSequenceAsync();
        expectedMax.HasValue.ShouldBeTrue();

        var capability = host.Services.GetRequiredService<IEventStore>();
        var usage = await capability.TryCreateUsage(CancellationToken.None);

        usage.ShouldNotBeNull();
        usage.MaxEventSequence.ShouldBe(expectedMax);
    }
}
