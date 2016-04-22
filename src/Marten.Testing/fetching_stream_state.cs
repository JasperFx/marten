using System;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Events;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
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
        }

        [Fact]
        public async Task can_fetch_the_stream_version_and_aggregate_type_async()
        {
            var state = await theSession.Events.FetchStreamStateAsync(theStreamId).ConfigureAwait(false);

            state.Id.ShouldBe(theStreamId);
            state.Version.ShouldBe(2);
            state.AggregateType.ShouldBe(typeof(Quest));
        }
    }
}