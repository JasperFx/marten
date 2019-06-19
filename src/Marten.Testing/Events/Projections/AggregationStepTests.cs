using System;
using System.Linq.Expressions;
using Baseline.Reflection;
using Marten.Events;
using Marten.Events.Projections;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class AggregationStepTests
    {
        [Fact]
        public void can_build_aggregation_step_for_an_apply_method()
        {
            Expression<Action<QuestParty, MembersJoined>> apply = (p, j) => p.Apply(j);

            var joined = ReflectionHelper.GetMethod<QuestParty>(x => x.Apply(new MembersJoined()));

            var joinedStep = new AggregationStep<QuestParty, MembersJoined>(joined);

            var party = new QuestParty();
            var joinedEvent = new MembersJoined { Members = new[] { "Wolverine", "Cyclops", "Nightcrawler" } };

            joinedStep.Apply(party, joinedEvent);

            party.Members.ShouldHaveTheSameElementsAs(joinedEvent.Members);
        }

        [Fact]
        public void can_build_aggregation_step_for_an_event_apply_method()
        {
            Expression<Action<QuestPartyWithEvents, MembersJoined>> apply = (p, j) => p.Apply(new Event<MembersJoined>(j));

            var joined = ReflectionHelper.GetMethod<QuestPartyWithEvents>(x => x.Apply(new Event<MembersJoined>(new MembersJoined())));

            var joinedStep = new EventAggregationStep<QuestPartyWithEvents, MembersJoined>(joined);

            var party = new QuestPartyWithEvents();
            var joinedEvent = new MembersJoined { Members = new[] { "Wolverine", "Cyclops", "Nightcrawler" } };

            joinedStep.Apply(party, new Event<MembersJoined>(joinedEvent));

            party.Members.ShouldHaveTheSameElementsAs(joinedEvent.Members);
        }
    }
}
