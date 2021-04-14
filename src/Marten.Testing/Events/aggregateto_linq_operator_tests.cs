using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Events
{
    public class aggregateTo_linq_operator_tests: DestructiveIntegrationContext
    {
        private readonly MembersJoined _joined1 = new() { Members = new[] { "Rand", "Matrim", "Perrin", "Thom" } };
        private readonly MembersDeparted _departed1 = new() { Members = new[] {"Thom"} };

        private readonly MembersJoined _joined2 = new() { Members = new[] {"Elayne", "Moiraine", "Elmindreda"} };
        private readonly MembersDeparted _departed2 = new() { Members = new[] {"Moiraine"} };

        [Fact]
        public void can_aggregate_events_to_aggregate_type_synchronously()
        {
            theSession.Events.StartStream<Quest>(_joined1, _departed1);
            theSession.Events.StartStream<Quest>(_joined2, _departed2);
            theSession.SaveChanges();

            var questParty = theSession.Events.QueryAllRawEvents().AggregateTo<QuestParty>();

            questParty.Members.ShouldHaveTheSameElementsAs("Rand", "Matrim", "Perrin", "Elayne", "Elmindreda");
        }

        [Fact]
        public async Task can_aggregate_events_to_aggregate_type_asynchronously()
        {
            theSession.Events.StartStream<Quest>(_joined1, _departed1);
            theSession.Events.StartStream<Quest>(_joined2, _departed2);
            await theSession.SaveChangesAsync();

            var questParty = await theSession.Events.QueryAllRawEvents().AggregateToAsync<QuestParty>();

            questParty.Members.ShouldHaveTheSameElementsAs("Rand", "Matrim", "Perrin", "Elayne", "Elmindreda");
        }

        [Fact]
        public void can_aggregate_with_initial_state_synchronously()
        {
            var initialParty = new QuestParty { Members = new List<string> { "Lan" } };
            theSession.Events.StartStream<Quest>(_joined1, _departed1);
            theSession.Events.StartStream<Quest>(_joined2, _departed2);
            theSession.SaveChanges();

            var questParty = theSession.Events.QueryAllRawEvents().AggregateTo(initialParty);

            questParty.Members.ShouldHaveTheSameElementsAs("Lan", "Rand", "Matrim", "Perrin", "Elayne", "Elmindreda");
        }

        [Fact]
        public async Task can_aggregate_with_initial_state_asynchronously()
        {
            var initialParty = new QuestParty { Members = new List<string> { "Lan" } };
            theSession.Events.StartStream<Quest>(_joined1, _departed1);
            theSession.Events.StartStream<Quest>(_joined2, _departed2);
            await theSession.SaveChangesAsync();

            var questParty = await theSession.Events.QueryAllRawEvents().AggregateToAsync(initialParty);

            questParty.Members.ShouldHaveTheSameElementsAs("Lan", "Rand", "Matrim", "Perrin", "Elayne", "Elmindreda");
        }

        public aggregateTo_linq_operator_tests(DefaultStoreFixture fixture): base(fixture)
        {
            theStore.Advanced.Clean.DeleteAllEventData();
        }
    }
}
