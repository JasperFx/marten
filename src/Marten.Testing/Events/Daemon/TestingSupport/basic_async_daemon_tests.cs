using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
}
