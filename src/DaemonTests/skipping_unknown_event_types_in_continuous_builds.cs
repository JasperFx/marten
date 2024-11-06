using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.Coordination;
using Marten.Events.Daemon.Resiliency;
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

        using (var session = _processor.Services.GetRequiredService<IDocumentStore>().LightweightSession())
        {
            var aggregate = await session.LoadAsync<MyAggregate>(streamId);
            aggregate.ShouldNotBeNull();
            aggregate.BCount.ShouldBe(2);
            aggregate.CCount.ShouldBe(1);
            aggregate.DCount.ShouldBe(1);
        }
    }
}

public class WeirdCustomAggregation: CustomProjection<MyAggregate, Guid>
{
    public WeirdCustomAggregation()
    {
        ProjectionName = "Weird";
    }

    public override ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<MyAggregate, Guid> slice, CancellationToken cancellation,
        ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
    {
        slice.Aggregate ??= new MyAggregate{Id = slice.Id};
        foreach (var e in slice.Events())
        {
            switch (e.Data)
            {
                case AEvent:
                    slice.Aggregate.ACount++;
                    break;
                case BEvent:
                    slice.Aggregate.BCount++;
                    break;
                case CEvent:
                    slice.Aggregate.CCount++;
                    break;
                case DEvent:
                    slice.Aggregate.DCount++;
                    break;
            }
        }

        session.Store(slice.Aggregate);

        return new ValueTask();
    }
}
