using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace EventPublisher;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.AddConsole();

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddRuntimeInstrumentation().AddMeter("EventPublisher");
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        var endpointUri = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        Console.WriteLine("OLTP endpoint: " + endpointUri);

        builder.Services.AddOpenTelemetry().UseOtlpExporter();

        // The following lines enable the Prometheus exporter (requires the OpenTelemetry.Exporter.Prometheus.AspNetCore package)
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.AddSource("Marten");
            })
            // BUG: Part of the workaround for https://github.com/open-telemetry/opentelemetry-dotnet-contrib/issues/1617
            .WithMetrics(metrics =>
            {
                metrics.AddPrometheusExporter(options => options.DisableTotalNameSuffixForCounters = true);
                metrics.AddMeter("EventPublisher");
                metrics.AddMeter("Marten");
            });

        builder.Services.AddHostedService<HostedPublisher>();

        builder.Services.AddMarten(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.DatabaseSchemaName = "cli";
            opts.DisableNpgsqlLogging = true;

            opts.OpenTelemetry.TrackConnections = TrackLevel.Verbose;

            opts.OpenTelemetry.TrackEventCounters();

            opts.MultiTenantedWithSingleServer(
                ConnectionSource.ConnectionString,
                t => t.WithTenants("tenant1", "tenant2", "tenant3")
            );
        });

        await builder.Build().RunAsync();
    }
}

internal class HostedPublisher: BackgroundService
{
    private readonly IServiceProvider _container;
    private readonly Counter<long> _counter;
    private readonly Meter _meter;

    public HostedPublisher(IServiceProvider container)
    {
        _container = container;
        _meter = new Meter("EventPublisher");
        _counter = _meter.CreateCounter<long>("events_published", "events", "Number of Events published");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var source = new TaskCompletionSource();

        var stores = _container.AllDocumentStores();
        var board = new StatusBoard(source.Task);

        var tasks = new List<Task>();
        foreach (var store in stores)
        {
            await store.Advanced.Clean.CompletelyRemoveAllAsync(stoppingToken);

            var databases = await store.Storage.AllDatabases();
            foreach (var database in databases)
            {
                for (var i = 0; i < 10; i++)
                {
                    var publisher = new Publisher(store, database, board);
                    tasks.Add(publisher.Start(_counter));
                }
            }
        }

        await Task.WhenAll(tasks.ToArray());
    }
}

internal class Publisher
{
    private readonly StatusBoard _board;
    private readonly IMartenDatabase _database;
    private readonly string _name;
    private readonly IDocumentStore _store;

    public Publisher(IDocumentStore store, IMartenDatabase database, StatusBoard board)
    {
        _store = store;
        _database = database;
        _board = board;

        var storeName = store.GetType() == typeof(DocumentStore) ? "Marten" : store.GetType().NameInCode();
        _name = $"{storeName}:{_database.Identifier}";
    }

    public Task Start(Counter<long> counter)
    {
        var random = Random.Shared;
        return Task.Run(async () =>
        {
            while (true)
            {
                var delay = random.Next(0, 250);

                await Task.Delay(delay.Milliseconds());
                await PublishEvents(counter);
            }
        });
    }

    public async Task PublishEvents(Counter<long> counter)
    {
        var streams = TripStream.RandomStreams(5);
        while (streams.Any())
        {
            var count = 0;
            var options = SessionOptions.ForDatabase(_database);

            await using var session = _store.LightweightSession(options);
            foreach (var stream in streams.ToArray())
            {
                if (stream.TryCheckOutEvents(out var events))
                {
                    count += events.Length;

                    counter.Add(events.Length);

                    session.Events.Append(stream.StreamId, events);
                }

                if (stream.IsFinishedPublishing())
                {
                    streams.Remove(stream);
                }
            }

            await session.SaveChangesAsync();
            _board.Update(_name, count);
        }
    }
}
