using System;
using System.Threading.Tasks;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    // SAMPLE: fetching_stream_state
    public class fetching_stream_state : DocumentSessionFixture<NulloIdentityMap>
    {
        private Guid theStreamId;

        public fetching_stream_state()
        {
            var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            theStreamId = theSession.Events.StartStream<Quest>(joined, departed);
            theSession.SaveChanges();
        }

        [Fact]
        public void can_fetch_the_stream_version_and_aggregate_type()
        {
            var state = theSession.Events.FetchStreamState(theStreamId);

            state.Id.ShouldBe(theStreamId);
            state.Version.ShouldBe(2);
            state.AggregateType.ShouldBe(typeof(Quest));
            state.LastTimestamp.ShouldNotBe(DateTime.MinValue);
        }

        [Fact]
        public async Task can_fetch_the_stream_version_and_aggregate_type_async()
        {
            var state = await theSession.Events.FetchStreamStateAsync(theStreamId).ConfigureAwait(false);

            state.Id.ShouldBe(theStreamId);
            state.Version.ShouldBe(2);
            state.AggregateType.ShouldBe(typeof(Quest));
            state.LastTimestamp.ShouldNotBe(DateTime.MinValue);
        }

        [Fact]
        public async Task can_fetch_the_stream_version_through_batch_query()
        {
            var batch = theSession.CreateBatchQuery();

            var stateTask = batch.Events.FetchStreamState(theStreamId);

            await batch.Execute().ConfigureAwait(false);

            var state = await stateTask.ConfigureAwait(false);

            state.Id.ShouldBe(theStreamId);
            state.Version.ShouldBe(2);
            state.AggregateType.ShouldBe(typeof(Quest));
            state.LastTimestamp.ShouldNotBe(DateTime.MinValue);

        }

        [Fact]
        public async Task can_fetch_the_stream_events_through_batch_query()
        {
            var batch = theSession.CreateBatchQuery();

            var eventsTask = batch.Events.FetchStream(theStreamId);

            await batch.Execute().ConfigureAwait(false);

            var events = await eventsTask.ConfigureAwait(false);

            events.Count.ShouldBe(2);
        }
    }
    // ENDSAMPLE
}