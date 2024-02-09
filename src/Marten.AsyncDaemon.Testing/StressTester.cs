using System;
using System.Threading;
using System.Threading.Tasks;
using Castle.Core.Logging;
using JasperFx.Core;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.Resiliency;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing;

public class StressTester
{
    private readonly ITestOutputHelper _output;

    public StressTester(ITestOutputHelper output)
    {
        _output = output;
    }

    //[Fact]
    public async Task try_to_exhaust_connection_count()
    {
        var logger = new TestOutputMartenLogger(_output);

        using var store = DocumentStore.For(options =>
        {
            options.DatabaseSchemaName = "stress";
            options.Connection(ConnectionSource.ConnectionString);


            options.Logger(logger);

            options.Projections.AsyncMode = DaemonMode.Solo;
            options.Events.AddEventType(typeof(Event1));
            options.Events.AddEventType(typeof(Event2));
            options.Events.AddEventType(typeof(Event3));
            options.Events.AddEventType(typeof(Event4));

            options.Projections.Add<View1Projection>(Marten.Events.Projections.ProjectionLifecycle.Async);
            options.Projections.Add<View2Projection>(Marten.Events.Projections.ProjectionLifecycle.Async);
        });

        var stopping = new CancellationTokenSource();
        stopping.CancelAfter(5.Minutes());

        using var timer = new System.Timers.Timer(100);
        timer.Elapsed += (e, v) =>
        {
            using var session = store.LightweightSession();
            session.Events.StartStream(new Event1());
            session.SaveChanges();

        };
        timer.Start();

        using var daemon = await store.BuildProjectionDaemonAsync(logger:logger);
        await daemon.StartAllAsync();

        long lastCount = 0;
        while (!stopping.IsCancellationRequested)
        {
            await Task.Delay(250, stopping.Token);
            await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await conn.OpenAsync(stopping.Token);
            var count = (long)(await conn.CreateCommand("select count(*) from pg_stat_activity;")
                .ExecuteScalarAsync(stopping.Token))!;

            if (count != lastCount)
            {
                _output.WriteLine($">>>>>>>>>>>>>>>>>>>>>>>>>>>> open connections increased to {count} <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<");
                lastCount = count;
            }

            _output.WriteLine($">>> {count} open connections");

            count.ShouldBeLessThan(25);
        }

        await Task.Delay(30.Seconds());


    }
}

public class PublishService : BackgroundService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<PublishService> _logger;
    public PublishService(IDocumentStore store, ILogger<PublishService> logger)
    {
        _store = store;
        _logger = logger;
    }
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("starting PublishService");
        return Task.Run(() =>
        {
            var timer = new System.Timers.Timer(1000);
            timer.Elapsed += (e, v) =>
            {
                _logger.LogInformation("Inserting event");
                using var session = _store.LightweightSession();
                session.Events.StartStream(new Event1());
                session.SaveChanges();
                session.Dispose();
            };
            timer.Start();

        },stoppingToken);
    }
}

public class Event1
{
    public Guid Id { get; set; }
    public int Number { get; set; }
}

public class Event2
{
    public Guid Id { get; set; }
    public int Number { get; set; }
}
public class Event3
{
    public Guid Id { get; set; }
    public int Number { get; set; }
}

public class Event4
{
    public Guid Id { get; set; }
    public int Number { get; set; }
}

public class View1
{
    public Guid Id { get; set; }
    public bool IsEvent1Applied { get; set; }
    public bool IsEvent2Applied { get; set; }
}

public class View2
{
    public Guid Id { get; set; }
    public bool IsEvent3Applied { get; set; }
    public bool IsEvent4Applied { get; set; }
}

public class View1Projection : SingleStreamProjection<View1>
{
    public void Apply(View1 v, Event1 e)
    {
        v.IsEvent1Applied = true;
    }
    public void Apply(View1 v, Event2 e)
    {
        v.IsEvent2Applied = true;
    }
}
public class View2Projection : SingleStreamProjection<View2>
{

    public void Apply(View2 v, Event3 e)
    {
        v.IsEvent3Applied = true;
    }
    public void Apply(View2 v, Event4 e)
    {
        v.IsEvent4Applied = true;
    }
}
