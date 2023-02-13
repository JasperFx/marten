using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lamar;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Weasel.Core;

namespace EventPublisher;

internal static class Program
{
    static async Task Main(string[] args)
    {
        await using var container = BuildContainer();

        var source = new TaskCompletionSource();

        var stores = container.AllDocumentStores();
        var board = new StatusBoard(source.Task);

        var tasks = new List<Task>();
        foreach (var store in stores)
        {
            await store.Advanced.Clean.CompletelyRemoveAllAsync();

            var databases = await store.Storage.AllDatabases();
            foreach (var database in databases)
            {
                for (var i = 0; i < 10; i++)
                {
                    var publisher = new Publisher(store, database, board);
                    tasks.Add(publisher.Start());
                }
            }
        }

        await Task.WhenAll(tasks.ToArray());
    }

    public static IContainer BuildContainer()
    {
        return new Container(services =>
        {
            services.AddMarten(opts =>
            {
                opts.AutoCreateSchemaObjects = AutoCreate.All;
                opts.DatabaseSchemaName = "cli";

                opts.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString)
                    .WithTenants("tenant1", "tenant2", "tenant3");
            });
        });
    }
}

internal class Publisher
{
    private readonly IDocumentStore _store;
    private readonly IMartenDatabase _database;
    private readonly StatusBoard _board;
    private readonly string _name;

    public Publisher(IDocumentStore store, IMartenDatabase database, StatusBoard board)
    {
        _store = store;
        _database = database;
        _board = board;

        var storeName = store.GetType() == typeof(DocumentStore) ? "Marten" : store.GetType().NameInCode();
        _name = $"{storeName}:{_database.Identifier}";
    }

    public Task Start()
    {
        var random = new Random();
        return Task.Run(async () =>
        {
            while (true)
            {
                var delay = random.Next(0, 250);

                await Task.Delay(delay.Milliseconds());
                await PublishEvents();
            }
        });
    }

    public async Task PublishEvents()
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
