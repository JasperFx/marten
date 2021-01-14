using System;
using System.Threading.Tasks;
using Marten.Testing.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class event_statistics : IntegrationContext
    {
        public event_statistics(DefaultStoreFixture fixture) : base(fixture)
        {
            theStore.Advanced.Clean.DeleteAllEventData();
        }

        [Fact]
        public async Task fetch_from_empty_store()
        {
            var statistics = await theStore.Events.FetchStatistics();

            statistics.EventCount.ShouldBe(0);
            statistics.StreamCount.ShouldBe(0);
            statistics.EventSequenceNumber.ShouldBe(1);
        }

        [Fact]
        public async Task fetch_from_non_empty_event_store()
        {
            theSession.Events.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());
            theSession.Events.Append(Guid.NewGuid(), new AEvent(), new CEvent(), new DEvent());
            theSession.Events.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());
            theSession.Events.Append(Guid.NewGuid(), new BEvent(), new CEvent(), new DEvent());
            theSession.Events.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());

            await theSession.SaveChangesAsync();

            var statistics = await theStore.Events.FetchStatistics();

            statistics.EventCount.ShouldBe(18);
            statistics.StreamCount.ShouldBe(5);
            statistics.EventSequenceNumber.ShouldBe(18);
        }

    }
}
