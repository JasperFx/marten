using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Baseline.Dates;
using Marten;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Testing.Harness;

namespace EventPublisher
{
    class Program
    {
        static Task Main(string[] args)
        {
            using var store = DocumentStore.For(opts =>
            {
                opts.AutoCreateSchemaObjects = AutoCreate.All;
                opts.DatabaseSchemaName = "cli";
                opts.Connection(ConnectionSource.ConnectionString);
            });

            store.Advanced.Clean.CompletelyRemoveAll();

            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                var task = new Publisher(store).Start();
                tasks.Add(task);
            }

            return Task.WhenAll(tasks.ToArray());

        }

        public class Publisher
        {
            private readonly IDocumentStore _store;

            public Publisher(IDocumentStore store)
            {
                _store = store;
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
                    using (var session = _store.LightweightSession())
                    {
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
                        Console.WriteLine($"Wrote {count} events at {DateTime.Now.ToShortTimeString()}");
                    }


                }

            }
        }
    }
}
