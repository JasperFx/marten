using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baseline.ImTools;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing
{
    public class event_fetcher_tests : OneOffConfigurationsContext, IAsyncLifetime
    {
        private readonly List<ISqlFragment> theFilters = new List<ISqlFragment>();
        private readonly EventRange theRange;
        private readonly ShardName theShardName = new ShardName("foo", "All");

        public event_fetcher_tests()
        {
            theRange = new EventRange(theShardName, 0, 100);
        }

        internal async Task executeAfterLoadingEvents(Action<IEventStore> loadEvents)
        {
            loadEvents(theSession.Events);
            await theSession.SaveChangesAsync();

            var fetcher = new EventFetcher(theStore, null, theStore.Tenancy.Default.Database, theFilters.ToArray());
            await fetcher.Load(theRange, default);
        }


        [Fact]
        public async Task simple_fetch_with_guid_identifiers()
        {
            var stream = Guid.NewGuid();
            await executeAfterLoadingEvents(e =>
            {

                e.Append(stream, new AEvent(), new BEvent(), new CEvent(), new DEvent());
            });

            await theSession.SaveChangesAsync();

            theRange.Events.Count.ShouldBe(4);
            var @event = theRange.Events[0];
            @event.StreamId.ShouldBe(stream);
            @event.Version.ShouldBe(1);
            @event.Data.ShouldBeOfType<AEvent>();
        }

        [Fact]
        public async Task simple_fetch_with_string_identifiers()
        {
            StoreOptions(x => x.Events.StreamIdentity = StreamIdentity.AsString);

            var stream = Guid.NewGuid().ToString();
            await executeAfterLoadingEvents(e =>
            {

                e.Append(stream, new AEvent(), new BEvent(), new CEvent(), new DEvent());
            });

            await theSession.SaveChangesAsync();

            theRange.Events.Count.ShouldBe(4);
            var @event = theRange.Events[0];
            @event.StreamKey.ShouldBe(stream);
            @event.Version.ShouldBe(1);
            @event.Data.ShouldBeOfType<AEvent>();
        }

        [Fact]
        public async Task should_get_the_aggregate_type_name_if_exists()
        {
            await executeAfterLoadingEvents(e =>
            {
                e.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());
                e.StartStream<Letters>(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent(),
                    new DEvent());

            });

            for (var i = 0; i < 4; i++)
            {
                theRange.Events[i].AggregateTypeName.ShouldBeNull();
            }

            for (var i = 4; i < theRange.Events.Count; i++)
            {
                theRange.Events[i].AggregateTypeName.ShouldBe("letters");
            }
        }


        [Fact]
        public async Task filter_on_aggregate_type_name_if_exists()
        {
            theFilters.Add(new AggregateTypeFilter(typeof(Letters), theStore.Events));

            await executeAfterLoadingEvents(e =>
            {
                e.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());
                e.StartStream<Letters>(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent(),
                    new DEvent());

            });

            theRange.Events.Count.ShouldBe(5);
            foreach (var @event in theRange.Events)
            {
                @event.AggregateTypeName.ShouldBe("letters");
            }

        }

        public Task InitializeAsync()
        {
            return theStore.Advanced.Clean.DeleteAllEventDataAsync();
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }
    }

    public class Letters
    {
        public int ACount { get; set; }
        public int BCount { get; set; }
        public int CCount { get; set; }
        public int DCount { get; set; }
    }
}
