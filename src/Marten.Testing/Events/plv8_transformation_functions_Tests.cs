using System;
using System.Linq;
using Baseline;
using Marten.Schema;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class plv8_transformation_functions_Tests : IntegratedFixture
    {
        public plv8_transformation_functions_Tests()
        {
            var directory =
                AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().AppendPath("Events");

            var eventGraph = theContainer.GetInstance<IDocumentSchema>().Events;
            eventGraph.AddEventType(typeof (MembersJoined));

            eventGraph.AddEventType(typeof (EventA));
            


            var theEvents = theStore.EventStore;

            theEvents.RebuildEventStoreSchema();
            theEvents.LoadProjections(directory);
            theEvents.InitializeEventStoreInDatabase(true);
            
        }

        [Fact]
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

            using (var session = theStore.OpenSession())
            {
                var json = session.Events.Transforms.Transform("location", stream, joined);
                var expectation = "{'Day':3,'Location':'Baerlon','Id':'EVENT','Quest':'STREAM'}"
                    .Replace("EVENT", joined.Id.ToString())
                    .Replace("STREAM", stream.ToString())
                    .Replace('\'', '"');

                json.ShouldBe(expectation);
            }
        }

        [Fact]
        public void start_an_aggregate()
        {
            using (var session = theStore.OpenSession())
            {
                var aggregate = session.Events.Transforms.StartSnapshot<FakeAggregate>(new EventA {Name = "Alex Smith"});

                aggregate.ANames.Single().ShouldBe("Alex Smith");
            }
        }

        [Fact]
        public void alter_an_existing_aggregate()
        {
            var aggregate = new FakeAggregate
            {
                ANames = new[] {"Jamaal Charles", "Tamba Hali"},
                Id = Guid.NewGuid()
            };

            using (var session = theStore.OpenSession())
            {
                var snapshotted = session.Events.Transforms.ApplySnapshot(aggregate, new EventA {Name = "Eric Fisher"});

                snapshotted.Id.ShouldBe(aggregate.Id);
                snapshotted.ANames.ShouldHaveTheSameElementsAs("Jamaal Charles", "Tamba Hali", "Eric Fisher");
            }
        }
    }
}