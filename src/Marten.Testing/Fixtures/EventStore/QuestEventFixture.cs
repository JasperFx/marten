using System;
using System.Collections.Generic;
using Marten.Events;
using Marten.Testing.Events;
using Marten.Util;
using StoryTeller;

namespace Marten.Testing.Fixtures.EventStore
{
    [Hidden]
    public class QuestEventFixture : Fixture
    {
        private List<object> _events;
        private DateTime _time;

        public override void SetUp()
        {
            _events = new List<object>();
        }

        [FormatAs("Members {names} joined the quest on day {day} at {location}")]
        public void MembersJoinedAt(string[] names, int day, string location)
        {
            var @event = new MembersJoined {Day = day, Members = names, Location = location};
            _events.Add(@event);
        }

        [FormatAs("Members {names} departed the quest on day {day} at {location}")]
        public void MembersDepartedAt(string[] names, int day, string location)
        {
            var @event = new MembersDeparted { Day = day, Members = names, Location = location };
            _events.Add(@event);
        }

        [FormatAs("The quest party arrived at {location} on day {day}")]
        public void Arrived(string location, int day)
        {
            var @event = new ArrivedAtLocation {Day = day, Location = location};
            _events.Add(@event);
        }

        public override void TearDown()
        {
            var store = Context.State.Retrieve<IDocumentStore>();
            var streamId = Context.State.Retrieve<Guid>("streamId");

            using (var session = store.LightweightSession())
            {
                // TODO -- see if we need to put in DateTime here
                session.Events.AppendEvents(streamId, _events.ToArray());
                session.SaveChanges();
            }
        }
    }
}