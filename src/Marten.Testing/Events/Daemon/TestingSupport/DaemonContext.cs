using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Daemon.TestingSupport
{

    public class TryItOut: DaemonContext
    {
        [Fact]
        public async Task publish_single_file()
        {
            NumberOfStreams = 10;
            await PublishSingleThreaded();

            var statistics = await theStore.Events.FetchStatistics();

            statistics.EventCount.ShouldBe(NumberOfEvents);
            statistics.StreamCount.ShouldBe(NumberOfStreams);
        }

        [Fact]
        public async Task try_build_everything()
        {
            NumberOfStreams = 10;
            await BuildAllExpectedAggregates();


        }


        [Fact]
        public async Task publish_multi_threaded()
        {
            NumberOfStreams = 100;
            await PublishMultiThreaded(10);

            var statistics = await theStore.Events.FetchStatistics();

            statistics.EventCount.ShouldBe(NumberOfEvents);
            statistics.StreamCount.ShouldBe(NumberOfStreams);
        }




    }

    [Collection("daemon")]
    public abstract class DaemonContext : OneOffConfigurationsContext
    {
        protected DaemonContext() : base("daemon")
        {
            theStore.Advanced.Clean.DeleteAllEventData();
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

        protected async Task BuildAllExpectedAggregates()
        {
            StoreOptions(opts => opts.Events.Projections.Inline(new TripAggregation()));

            await PublishSingleThreaded();

            var data = await theSession.Query<Trip>().ToListAsync();
            var dict = data.ToDictionary(x => x.Id);

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
