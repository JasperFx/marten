using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Baseline.Reflection;
using Marten.Events.Projections;
using Xunit;
using Marten.Events;

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
            var joinedEvent = new MembersJoined {Members = new []{"Wolverine", "Cyclops", "Nightcrawler"}};

            joinedStep.Apply(party, joinedEvent);

            party.Members.ShouldHaveTheSameElementsAs(joinedEvent.Members);
        }

        [Fact]
        public void can_build_aggregation_step_for_an_event_apply_method()
        {
            Expression<Action<QuestParty, MembersDeparted>> apply = (p, j) => p.Apply(new Event<MembersDeparted>(j));


            var departed = ReflectionHelper.GetMethod<QuestParty>(x => x.Apply(new Event<MembersDeparted>(new MembersDeparted())));

            var departedStep = new EventAggregationStep<QuestParty, MembersDeparted>(departed);

            var party = new QuestParty();
            var joinedEvent = new MembersJoined { Members = new[] { "Wolverine", "Cyclops", "Nightcrawler" } };
            var departedEvent = new MembersDeparted { Members = new[] { "Nightcrawler" } };

            party.Apply(joinedEvent);
            departedStep.Apply(party, new Event<MembersDeparted>(departedEvent));

            party.Members.ShouldHaveTheSameElementsAs(joinedEvent.Members.Take(2));
        }
    }


}