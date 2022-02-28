using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests
{
    [Collection("string_identified_streams")]
    public class ScenarioCopyAndReplaceStream : StoreContext<StringIdentifiedStreamsFixture>, IAsyncLifetime
    {
        public ScenarioCopyAndReplaceStream(StringIdentifiedStreamsFixture fixture) : base(fixture)
        {

        }

        public Task InitializeAsync()
        {
            return theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        }

        public Task DisposeAsync()
        {
            Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public void SampleCopyAndTransformStream()
        {
            #region sample_scenario-copyandtransformstream-setup
            var started = new QuestStarted { Name = "Find the Orb" };
            var joined = new MembersJoined { Day = 2, Location = "Faldor's Farm", Members = new[] { "Garion", "Polgara", "Belgarath" } };
            var slayed1 = new MonsterSlayed { Name = "Troll" };
            var slayed2 = new MonsterSlayed { Name = "Dragon" };

            using (var session = theStore.OpenSession())
            {
                session.Events.StartStream<Quest>(started.Name,started, joined, slayed1, slayed2);
                session.SaveChanges();
            }
            #endregion

            #region sample_scenario-copyandtransformstream-transform
            using (var session = theStore.OpenSession())
            {
                var events = session.Events.FetchStream(started.Name);

                var transformedEvents = events.SelectMany(x =>
                {
                    // Reapply existing metadata
                    session.Events.CopyMetadata(x, x.Data);

                    switch (x.Data)
                    {
                        case MonsterSlayed monster:
                        {
                            // Trolls we remove from our transformed stream
                            if (monster.Name.Equals("Troll")) return Array.Empty<object>();

                            session.Events.ApplyHeader("copied_from_event", x.Id, monster);
                            return new[] { monster };
                        }
                        case MembersJoined members:
                        {
                            // MembersJoined events we transform into a series of events
                            var membersEvents = MemberJoined.From(members).Cast<object>().ToArray();
                            session.Events.ApplyHeader("copied_from_event", x.Id, events: membersEvents);
                            return membersEvents;
                        }
                    }

                    session.Events.ApplyHeader("copied_from_event", x.Id, x.Data);
                    return new[] { x.Data };
                }).Where(x => x != null).ToArray();

                // Add "moved from" header to all events being written to new stream 
                session.Events.ApplyHeader("moved_from_stream", started.Name, transformedEvents);

                var moveTo = $"{started.Name} without Trolls";
                // Mark the old stream as moved.
                // This is done first in order for inline projections to handle the StreamMovedTo event before the moved events
                // Furthermore, we assert on the new expected stream version to guard against any racing updates
                session.Events.Append(started.Name, events.Count + 1, new StreamMovedTo
                {
                    To = moveTo
                });


                // We copy the transformed events to a new stream
                session.Events.StartStream<Quest>(moveTo, transformedEvents);

                // Transactionally update the streams.
                session.SaveChanges();
            }
            #endregion

            using (var session = theStore.OpenSession())
            {
                var events = session.Events.FetchStream($"{started.Name} without Trolls");
                foreach (var @event in events)
                {
                    @event.GetHeader("moved_from_stream").ToString().ShouldBe(started.Name);
                }
            }
        }

        #region sample_scenario-copyandtransformstream-newevent
        public class MemberJoined
        {
            public int Day { get; set; }
            public string Location { get; set; }
            public string Name { get; set; }

            public MemberJoined()
            {
            }

            public MemberJoined(int day, string location, string name)
            {
                Day = day;
                Location = location;
                Name = name;
            }

            public static MemberJoined[] From(MembersJoined @event)
            {
                return @event.Members.Select(x => new MemberJoined(@event.Day, @event.Location, x)).ToArray();
            }
        }
        #endregion

        #region sample_scenario-copyandtransformstream-streammoved
        public class StreamMovedTo
        {
            public string To { get; set; }
        }
        #endregion
    }
}
