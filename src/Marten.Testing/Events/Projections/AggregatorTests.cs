using System;
using Baseline;
using Marten.Events;
using Marten.Events.Projections;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class AggregatorTests
    {
        private readonly Aggregator<QuestParty> theAggregator = new Aggregator<QuestParty>();

        [Fact]
        public void event_types()
        {
            theAggregator.EventTypes.ShouldHaveTheSameElementsAs(typeof(MembersJoined), typeof(MembersDeparted), typeof(QuestStarted));
        }

        [Fact]
        public void can_derive_steps_for_apply_methods()
        {
            theAggregator.AggregatorFor<MembersJoined>().ShouldNotBeNull();
            theAggregator.AggregatorFor<MembersDeparted>().ShouldNotBeNull();
            theAggregator.AggregatorFor<QuestStarted>().ShouldNotBeNull();
        }

        [Fact]
        public void applies_to()
        {
            var stream = new EventStream(Guid.NewGuid(), false);

            theAggregator.AppliesTo(stream).ShouldBeFalse();

            stream.Add(new MonsterSlayed());

            theAggregator.AppliesTo(stream).ShouldBeFalse();

            stream.Add(new MembersJoined());

            theAggregator.AppliesTo(stream).ShouldBeTrue();
        }

        [Fact]
        public void explicitly_added_step_as_action()
        {
            theAggregator.Add<MonsterSlayed>((party, slayed) =>
            {
                party.Slayed.Fill(slayed.Name);
            });

            theAggregator.AppliesTo(new EventStream(Guid.NewGuid(), false).Add(new MonsterSlayed()))
                .ShouldBeTrue();

        }


        [Fact]
        public void add_special_step()
        {
            theAggregator.Add(new MonsterSlayer());

            theAggregator.AggregatorFor<MonsterSlayed>()
                .ShouldBeOfType<MonsterSlayer>();
        }

        [Fact]
        public void build_a_series_of_events()
        {
            var stream = new EventStream(Guid.NewGuid(), false)
                .Add(new QuestStarted {Name = "Destroy the Ring"})
                .Add(new MembersJoined {Members = new string[] {"Frodo", "Sam"}})
                .Add(new MembersJoined {Members = new string[] {"Merry", "Pippin"}})
                .Add(new MembersJoined {Members = new string[] {"Strider"}})
                .Add(new MembersJoined {Members = new string[] {"Gandalf", "Boromir", "Gimli", "Legolas"}})
                .Add(new MembersDeparted() {Members = new string[] {"Frodo", "Sam"}});

            var party = theAggregator.Build(stream.Events, null);

            party.Name.ShouldBe("Destroy the Ring");

            party.Members.ShouldHaveTheSameElementsAs("Merry", "Pippin", "Strider", "Gandalf", "Boromir", "Gimli", "Legolas");
        }

        public class MonsterSlayer : IAggregation<QuestParty, MonsterSlayed>
        {
            public void Apply(QuestParty aggregate, MonsterSlayed @event)
            {
                throw new NotImplementedException();
            }
        }
    }



    public class Aggregator_with_Event_metadata_Tests
    {
        private readonly Aggregator<QuestPartyWithEvents> theAggregator = new Aggregator<QuestPartyWithEvents>();

        [Fact]
        public void event_types()
        {
            theAggregator.EventTypes.ShouldHaveTheSameElementsAs(typeof(MembersJoined), typeof(MembersDeparted), typeof(QuestStarted));
        }

        [Fact]
        public void can_derive_steps_for_apply_methods()
        {
            theAggregator.AggregatorFor<MembersJoined>().ShouldNotBeNull();
            theAggregator.AggregatorFor<MembersDeparted>().ShouldNotBeNull();
            theAggregator.AggregatorFor<QuestStarted>().ShouldNotBeNull();
        }

        [Fact]
        public void applies_to()
        {
            var stream = new EventStream(Guid.NewGuid(), false);

            theAggregator.AppliesTo(stream).ShouldBeFalse();

            stream.Add(new MonsterSlayed());

            theAggregator.AppliesTo(stream).ShouldBeFalse();

            stream.Add(new MembersJoined());

            theAggregator.AppliesTo(stream).ShouldBeTrue();
        }

        [Fact]
        public void explicitly_added_step_as_action()
        {
            theAggregator.Add<MonsterSlayed>((party, slayed) =>
            {
                party.Slayed.Fill(slayed.Name);
            });

            theAggregator.AppliesTo(new EventStream(Guid.NewGuid(), false).Add(new MonsterSlayed()))
                .ShouldBeTrue();

        }

        [Fact]
        public void build_a_series_of_events()
        {
            var stream = new EventStream(Guid.NewGuid(), false)
                .Add(new QuestStarted { Name = "Destroy the Ring" })
                .Add(new MembersJoined { Members = new string[] { "Frodo", "Sam" } })
                .Add(new MembersJoined { Members = new string[] { "Merry", "Pippin" } })
                .Add(new MembersJoined { Members = new string[] { "Strider" } })
                .Add(new MembersJoined { Members = new string[] { "Gandalf", "Boromir", "Gimli", "Legolas" } })
                .Add(new MembersDeparted() { Members = new string[] { "Frodo", "Sam" } });

            var party = theAggregator.Build(stream.Events, null);

            party.Name.ShouldBe("Destroy the Ring");

            party.Members.ShouldHaveTheSameElementsAs("Merry", "Pippin", "Strider", "Gandalf", "Boromir", "Gimli", "Legolas");
        }

    }

    public class Aggregator_With_Provided_Aggregate_Object_Tests
    {
        private readonly Aggregator<QuestFinishingParty> theAggregator = new Aggregator<QuestFinishingParty>();

        [Fact]
        public void event_types()
        {
            theAggregator.EventTypes.ShouldHaveTheSameElementsAs(typeof(MembersEscaped), typeof(MembersJoined), typeof(MembersDeparted), typeof(QuestStarted));
        }

        [Fact]
        public void can_derive_steps_for_apply_methods()
        {
            theAggregator.AggregatorFor<MembersJoined>().ShouldNotBeNull();
            theAggregator.AggregatorFor<MembersDeparted>().ShouldNotBeNull();
            theAggregator.AggregatorFor<QuestStarted>().ShouldNotBeNull();
            theAggregator.AggregatorFor<MembersEscaped>().ShouldNotBeNull();
        }

        [Fact]
        public void applies_to()
        {
            var stream = new EventStream(Guid.NewGuid(), false);

            theAggregator.AppliesTo(stream).ShouldBeFalse();

            stream.Add(new MonsterSlayed());

            theAggregator.AppliesTo(stream).ShouldBeFalse();

            stream.Add(new MembersJoined());

            theAggregator.AppliesTo(stream).ShouldBeTrue();
        }
        
        [Fact]
        public void uses_default_aggregate_constructor_if_no_instance_provided()
        {
            var stream = new EventStream(Guid.NewGuid(), false)
                .Add(new QuestStarted { Name = "Destroy the Ring" })
                .Add(new MembersJoined { Members = new string[] { "Frodo", "Sam" } })
                .Add(new MembersJoined { Members = new string[] { "Merry", "Pippin" } })
                .Add(new MembersJoined { Members = new string[] { "Strider" } })
                .Add(new MembersJoined { Members = new string[] { "Gandalf", "Boromir", "Gimli", "Legolas" } })
                .Add(new MembersEscaped { Members = new string[] { "Frodo", "Sam" }, Location = "Mt. Doom" });

            Exception<NullReferenceException>.ShouldBeThrownBy(() => theAggregator.Build(stream.Events, null));
        }

        [Fact]
        public void uses_provided_instance()
        {
            var stream = new EventStream(Guid.NewGuid(), false)
                .Add(new QuestStarted { Name = "Destroy the Ring" })
                .Add(new MembersJoined { Members = new string[] { "Frodo", "Sam" } })
                .Add(new MembersJoined { Members = new string[] { "Merry", "Pippin" } })
                .Add(new MembersJoined { Members = new string[] { "Strider" } })
                .Add(new MembersJoined { Members = new string[] { "Gandalf", "Boromir", "Gimli", "Legolas" } })
                .Add(new MembersEscaped { Members = new string[] { "Frodo", "Sam" }, Location = "Mt. Doom" });

            var state = new QuestFinishingParty("The Eagles!");

            var party = theAggregator.Build(stream.Events, null, state);

            party.Name.ShouldBe("Destroy the Ring");

            party.Members.ShouldHaveTheSameElementsAs("Merry", "Pippin", "Strider", "Gandalf", "Boromir", "Gimli", "Legolas");
        }
    }
}