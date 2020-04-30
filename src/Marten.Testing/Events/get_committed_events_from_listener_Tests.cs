using System;
using System.Linq;
using Marten.Testing.CoreFunctionality;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class get_committed_events_from_listener_Tests
    {
        [Fact]
        public void get_correct_events_from_single_stream()
        {
            var listener = new StubDocumentSessionListener();
            var store = InitStore(listener);

            var id = Guid.NewGuid();
            var started = new QuestStarted();

            using (var session = store.LightweightSession())
            {
                session.Events.StartStream<Quest>(id, started);
                session.SaveChanges();

                var events = listener.LastCommit
                    .GetEvents()
                    .ToList();

                events.Count.ShouldBe(1);
                events.ElementAt(0).Data.ShouldBeOfType<QuestStarted>();
            }

            using (var session = store.LightweightSession())
            {
                var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.Append(id, joined, departed);

                session.SaveChanges();

                var events = listener.LastCommit
                    .GetEvents()
                    .ToList();

                events.Count.ShouldBe(2);
                events.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                events.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
            }
        }

        [Fact]
        public void get_correct_events_across_multiple_stream()
        {
            var listener = new StubDocumentSessionListener();
            var store = InitStore(listener);

            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            using (var session = store.LightweightSession())
            {
                session.Events.StartStream<Quest>(id1, new QuestStarted { Id = id1 });
                session.Events.StartStream<Quest>(id2, new QuestStarted { Id = id2 });
                session.SaveChanges();

                var events = listener.LastCommit
                    .GetEvents()
                    .ToList();

                events.Count.ShouldBe(2);

                events.Select(x => x.Data).OfType<QuestStarted>().Any(x => x.Id == id1).ShouldBeTrue();
                events.Select(x => x.Data).OfType<QuestStarted>().Any(x => x.Id == id2).ShouldBeTrue();
            }

            using (var session = store.LightweightSession())
            {
                session.Events.Append(id1,
                     new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } },
                     new MembersDeparted { Members = new[] { "Thom" } });

                session.Events.Append(id2,
                     new MembersJoined { Members = new string[] { "Spock", "Kirk", "Picard" } },
                     new MembersJoined { Members = new string[] { "Riker" } },
                     new MembersDeparted { Members = new[] { "Kirk" } });

                session.SaveChanges();

                var events = listener.LastCommit
                    .GetEvents()
                    .ToList();

                events.Count.ShouldBe(5);
                events.Count(e => e.Data is MembersJoined).ShouldBe(3);
                events.Count(e => e.Data is MembersDeparted).ShouldBe(2);
            }
        }

        public static DocumentStore InitStore(IDocumentSessionListener listener)
        {
            DocumentStore.For(ConnectionSource.ConnectionString)
                .Advanced.Clean.CompletelyRemoveAll();

            var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Connection(ConnectionSource.ConnectionString);

                _.Events.AddEventType(typeof(MembersJoined));
                _.Events.AddEventType(typeof(MembersDeparted));

                _.Listeners.Add(listener);
            });

            return store;
        }
    }
}
