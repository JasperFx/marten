using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.Coordination;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Shouldly;
using Weasel.Postgresql;

namespace DaemonTests;

public class skipping_unknown_event_types_in_continuous_builds : IAsyncLifetime
{
    private IHost _appender;
    private IHost _processor;

    public async Task InitializeAsync()
    {
        await SchemaUtils.DropSchema(ConnectionSource.ConnectionString, "missing_events");

        _appender = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "missing_events";
                    opts.Events.MapEventType<AEvent>("TripleAAA");
                }).ApplyAllDatabaseChangesOnStartup();

            }).StartAsync();



    }

    public async Task DisposeAsync()
    {
        await _appender.StopAsync();
        await _processor.StopAsync();
    }

    [Fact]
    public async Task cleanly_skip_over_unknown_events()
    {
        var store = _appender.Services.GetRequiredService<IDocumentStore>();

        var streamId = Guid.NewGuid();

        using (var session = store.LightweightSession())
        {
            session.Events.StartStream<MyAggregate>(streamId, new AEvent(), new AEvent(), new BEvent(), new BEvent(),
                new CEvent(), new DEvent());
            await session.SaveChangesAsync();

            // Rig up a bad, unknown event type
            session.QueueSqlCommand(
                "update missing_events.mt_events set mt_dotnet_type = 'wrong, wrong' where version = 1");
            await session.SaveChangesAsync();
        }

        _processor = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "missing_events";
                    opts.Projections.Add(new WeirdCustomAggregation(), ProjectionLifecycle.Async);
                }).AddAsyncDaemon(DaemonMode.Solo);
            }).StartAsync();

        var daemon = _processor.Services.GetRequiredService<IProjectionCoordinator>().DaemonForMainDatabase();
        await daemon.WaitForShardToBeRunning("Weird:All", 10.Seconds());
        await daemon.WaitForNonStaleData(5.Seconds());

        await using (var session = _processor.DocumentStore().LightweightSession())
        {
            var aggregate = await session.LoadAsync<MyAggregate>(streamId);
            aggregate.ShouldNotBeNull();
            aggregate.BCount.ShouldBe(2);
            aggregate.CCount.ShouldBe(1);
            aggregate.DCount.ShouldBe(1);
        }
    }
}

public class WeirdCustomAggregation: MultiStreamProjection<MyAggregate, Guid>
{
    public WeirdCustomAggregation()
    {
        ProjectionName = "Weird";
    }

    public override MyAggregate Evolve(MyAggregate snapshot, Guid id, IEvent e)
    {
        snapshot ??= new MyAggregate(){ Id = id };
        switch (e.Data)
        {
            case AEvent:
                snapshot.ACount++;
                break;
            case BEvent:
                snapshot.BCount++;
                break;
            case CEvent:
                snapshot.CCount++;
                break;
            case DEvent:
                snapshot.DCount++;
                break;
        }

        return snapshot;
    }

}
