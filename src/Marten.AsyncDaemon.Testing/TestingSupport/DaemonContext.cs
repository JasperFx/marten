using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing.TestingSupport
{
    [Collection("daemon")]
    public abstract class DaemonContext : OneOffConfigurationsContext
    {
        protected DaemonContext(ITestOutputHelper output) : base("daemon")
        {
            theStore.Advanced.Clean.DeleteAllEventData();
            Logger = new TestLogger<IProjection>(output);
        }

        public ILogger<IProjection> Logger { get; }

        protected async Task<NodeAgent> StartNodeAgent()
        {
            var agent = new NodeAgent(theStore, Logger);

            await agent.StartAll();

            return agent;
        }

        public int NumberOfStreams
        {
            get
            {
                return _streams.Count;
            }
            set
            {
                _streams.Clear();
                _streams.AddRange(TripStream.RandomStreams(value));
            }
        }

        public long NumberOfEvents => _streams.Sum(x => x.Events.Count);

        private readonly List<TripStream> _streams = new List<TripStream>();

        protected StreamAction[] ToStreamActions()
        {
            return _streams.Select(x => x.ToAction(theStore.Events)).ToArray();
        }

        protected async Task CheckAllExpectedAggregatesAgainstActuals()
        {
            var actuals = await LoadAllAggregatesFromDatabase();



            foreach (var stream in _streams)
            {
                if (stream.Expected == null)
                {
                    actuals.ContainsKey(stream.StreamId).ShouldBeFalse();
                }
                else
                {
                    if (actuals.TryGetValue(stream.StreamId, out var actual))
                    {
                        stream.Expected.ShouldBe(actual);
                    }
                    else
                    {
                        throw new Exception("Missing expected aggregate");
                    }
                }
            }
        }

        protected async Task BuildAllExpectedAggregates()
        {
            StoreOptions(opts => opts.Events.Projections.Inline(new TripAggregation()));

            await PublishSingleThreaded();

            var dict = await LoadAllAggregatesFromDatabase();

            foreach (var stream in _streams)
            {
                if (dict.TryGetValue(stream.StreamId, out var trip))
                {
                    stream.Expected = trip;
                }
                else
                {
                    stream.Expected = null;
                }

            }
        }

        protected async Task<Dictionary<Guid, Trip>> LoadAllAggregatesFromDatabase()
        {
            var data = await theSession.Query<Trip>().ToListAsync();
            var dict = data.ToDictionary(x => x.Id);
            return dict;
        }

        protected async Task PublishSingleThreaded()
        {
            foreach (var stream in _streams)
            {
                using (var session = theStore.LightweightSession())
                {
                    session.Events.StartStream(stream.StreamId, stream.Events);
                    await session.SaveChangesAsync();
                }
            }
        }

        protected Task PublishMultiThreaded(int threads)
        {
            foreach (var stream in _streams)
            {
                stream.Reset();
            }

            var publishers = createPublishers(threads);

            var tasks = publishers.Select(x => x.PublishAll()).ToArray();
            return Task.WhenAll(tasks);
        }

        private List<EventPublisher> createPublishers(int threads)
        {
            var streamsPerPublisher = (int) Math.Floor((double) _streams.Count / threads);
            var index = 0;

            var publishers = new List<EventPublisher>();
            for (var i = 0; i < threads; i++)
            {
                publishers.Add(new EventPublisher(theStore));
            }


            foreach (var publisher in publishers)
            {
                publisher.Streams.AddRange(_streams.GetRange(index, streamsPerPublisher));
                index += streamsPerPublisher;
            }

            if (index < _streams.Count - 1)
            {
                publishers.Last().Streams.Add(_streams.Last());
            }

            return publishers;
        }

        public class EventPublisher
        {
            private readonly DocumentStore _store;
            private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>();

            public EventPublisher(DocumentStore store)
            {
                _store = store;
            }

            public IList<TripStream> Streams { get; } = new List<TripStream>();

            public Task PublishAll()
            {
                Task.Factory.StartNew(publishAll, TaskCreationOptions.AttachedToParent);
                return _completion.Task;
            }

            private async Task publishAll()
            {
                try
                {
                    while (Streams.Any() && !Streams.All(x => x.IsFinishedPublishing()))
                    {
                        using (var session = _store.LightweightSession())
                        {
                            foreach (var stream in Streams)
                            {
                                if (stream.TryCheckOutEvents(out var events))
                                {
                                    if (events.Length == 0) throw new DivideByZeroException();
                                    session.Events.Append(stream.StreamId, events);
                                }
                            }

                            await session.SaveChangesAsync();

                        }
                    }
                }
                catch (Exception e)
                {
                    _completion.SetException(e);
                }

                _completion.SetResult(true);
            }
        }
    }
}
