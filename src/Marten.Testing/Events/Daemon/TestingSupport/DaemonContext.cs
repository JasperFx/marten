using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events.Daemon;
using Marten.Linq.SqlGeneration;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Daemon.TestingSupport
{
    public class basic_async_daemon_tests: DaemonContext
    {
        [Fact]
        public async Task event_fetcher_simple_case()
        {
            using var fetcher = new EventFetcher(theStore, new ISqlFragment[0]);

            NumberOfStreams = 10;
            await PublishSingleThreaded();

            var range1 = new EventRange("foo", 0, 10);
            await fetcher.Load(range1, CancellationToken.None);

            var range2 = new EventRange("foo", 10, 20);
            await fetcher.Load(range2, CancellationToken.None);

            var range3 = new EventRange("foo", 20, 38);
            await fetcher.Load(range3, CancellationToken.None);

            range1.Events.Count.ShouldBe(10);
            range2.Events.Count.ShouldBe(10);
            range3.Events.Count.ShouldBe(18);
        }

        [Fact]
        public async Task use_type_filters()
        {
            NumberOfStreams = 10;
            await PublishSingleThreaded();

            using var fetcher1 = new EventFetcher(theStore, new ISqlFragment[0]);

            var range1 = new EventRange("foo", 0, NumberOfEvents);
            await fetcher1.Load(range1, CancellationToken.None);

            var uniqueTypeCount = range1.Events.Select(x => x.EventType).Distinct()
                .Count();

            uniqueTypeCount.ShouldBe(5);

            var filter = new EventTypeFilter(theStore.Events, new Type[] {typeof(Travel), typeof(Arrival)});
            using var fetcher2 = new EventFetcher(theStore, new ISqlFragment[]{filter});

            var range2 = new EventRange("foo", 0, NumberOfEvents);
            await fetcher2.Load(range2, CancellationToken.None);
            range2.Events
                .Select(x => x.EventType)
                .OrderBy(x => x.Name).Distinct()
                .ShouldHaveTheSameElementsAs(typeof(Arrival), typeof(Travel));
        }

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
