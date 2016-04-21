using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Baseline.Reflection;
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
            var joinedEvent = new MembersJoined {Members = new []{"Wolverine", "Cyclops", "Nightcrawler"}};

            joinedStep.Apply(party, joinedEvent);

            party.Members.ShouldHaveTheSameElementsAs(joinedEvent.Members);
        }
    }

    public class QuestParty
    {
        public readonly IList<string> Members = new List<string>();

        public void Apply(MembersJoined joined)
        {
            Members.Fill(joined.Members);
        }

        public void Apply(MembersDeparted departed)
        {
            Members.RemoveAll(x => departed.Members.Contains(x));
        }
    }
}