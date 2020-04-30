using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class Bug_705_FetchStreamState_before_AggregateStream: IntegrationContext
    {
        [Fact]
        public async Task call_fetch_stream_state_on_new_stream()
        {
            Guid id;

            using (var session = theStore.OpenSession())
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                id = session.Events.StartStream<Quest>(joined, departed).Id;
                await session.SaveChangesAsync();
            }

            using (var store2 = DocumentStore.For(ConnectionSource.ConnectionString))
            {
                using (var session = store2.OpenSession())
                {
                    var state = await session.Events.FetchStreamStateAsync(id);
                    state.Version.ShouldBe(2);
                }
            }
        }

        public Bug_705_FetchStreamState_before_AggregateStream(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
