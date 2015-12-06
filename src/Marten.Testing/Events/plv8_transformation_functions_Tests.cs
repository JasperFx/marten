using System;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Schema;
using Shouldly;
using StructureMap;

namespace Marten.Testing.Events
{
    public class plv8_transformation_functions_Tests
    {
        private readonly IEventStore theEvents;
        private IContainer theContainer;

        public plv8_transformation_functions_Tests()
        {
            theContainer = Container.For<DevelopmentModeRegistry>();

            var directory =
                AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().AppendPath("Events");

            var eventGraph = theContainer.GetInstance<IDocumentSchema>().Events;
            eventGraph.StreamMappingFor<Quest>()
                .AddEvent(typeof (MembersJoined));

            eventGraph.StreamMappingFor<FakeAggregate>().AddEvent(typeof (EventA));


            theEvents = theContainer.GetInstance<Marten.Events.IEventStore>();
            theEvents.Administration.RebuildEventStoreSchema();
            theEvents.Administration.LoadProjections(directory);
            theEvents.Administration.InitializeEventStoreInDatabase();

        }

        public void apply_a_simple_transformation()
        {
            var joined = new MembersJoined
            {
                Day = 3,
                Location = "Baerlon",
                Members = new[] {"Min"},
                Id = Guid.NewGuid()
            };

            var stream = Guid.NewGuid();
            var json = theEvents.Transforms.Transform("location", stream, joined);
            var expectation = "{'Day':3,'Location':'Baerlon','Id':'EVENT','Quest':'STREAM'}"
                .Replace("EVENT", joined.Id.ToString())
                .Replace("STREAM", stream.ToString())
                .Replace('\'', '"');

            json.ShouldBe(expectation);
        }

        public void start_an_aggregate()
        {
            var aggregate = theEvents.Transforms.StartSnapshot<FakeAggregate>(new EventA {Name = "Alex Smith"});

            aggregate.ANames.Single().ShouldBe("Alex Smith");
        }

        public void alter_an_existing_aggregate()
        {
            var aggregate = new FakeAggregate
            {
                ANames = new string[] {"Jamaal Charles", "Tamba Hali"},
                Id = Guid.NewGuid()

            };

            var snapshotted = theEvents.Transforms.ApplySnapshot(aggregate, new EventA {Name = "Eric Fisher"});

            snapshotted.Id.ShouldBe(aggregate.Id);
            snapshotted.ANames.ShouldHaveTheSameElementsAs("Jamaal Charles", "Tamba Hali", "Eric Fisher");
        }
    }
}