using Marten.Events;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class asserting_on_expected_event_version_on_append: DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void should_check_max_event_id_on_append()
        {
            var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            var stream = theSession.Events.StartStream<Quest>(joined).Id;
            theSession.Events.Append(stream, 2, departed);

            theSession.SaveChanges();

            var state = theSession.Events.FetchStreamState(stream);

            state.Id.ShouldBe(stream);
            state.Version.ShouldBe(2);
        }

        [Fact]
        public void should_not_append_events_when_unexpected_max_version()
        {
            var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            var stream = theSession.Events.StartStream<Quest>(joined).Id;
            theSession.SaveChanges();

            theSession.Events.Append(stream, 2, departed);

            using (var session = theStore.OpenSession())
            {
                var joined3 = new MembersJoined { Members = new[] { "Egwene" } };
                var departed3 = new MembersDeparted { Members = new[] { "Perrin" } };

                session.Events.Append(stream, joined3, departed3);
                session.SaveChanges();
            }

            Assert.Throws<EventStreamUnexpectedMaxEventIdException>(() => theSession.SaveChanges());

            using (var session = theStore.OpenSession())
            {
                var state = session.Events.FetchStreamState(stream);

                state.Id.ShouldBe(stream);
                state.Version.ShouldBe(3);
            }
        }

        [Fact]
        public void should_check_max_event_id_on_append_with_string_identifier()
        {
            StoreOptions(_ => _.Events.StreamIdentity = StreamIdentity.AsString);

            var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            var stream = "First";

            theSession.Events.Append(stream, joined);
            theSession.Events.Append(stream, 2, departed);

            theSession.SaveChanges();

            var state = theSession.Events.FetchStreamState(stream);

            state.Key.ShouldBe(stream);
            state.Version.ShouldBe(2);
        }

        [Fact]
        public void should_not_append_events_when_unexpected_max_version_with_string_identifier()
        {
            StoreOptions(_ => _.Events.StreamIdentity = StreamIdentity.AsString);

            var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            var stream = "Another";
            theSession.Events.Append(stream, joined);
            theSession.SaveChanges();

            theSession.Events.Append(stream, 2, departed);

            using (var session = theStore.OpenSession())
            {
                var joined3 = new MembersJoined { Members = new[] { "Egwene" } };
                var departed3 = new MembersDeparted { Members = new[] { "Perrin" } };

                session.Events.Append(stream, joined3, departed3);
                session.SaveChanges();
            }

            Assert.Throws<EventStreamUnexpectedMaxEventIdException>(() => theSession.SaveChanges());

            using (var session = theStore.OpenSession())
            {
                var state = session.Events.FetchStreamState(stream);

                state.Key.ShouldBe(stream);
                state.Version.ShouldBe(3);
            }
        }
    }
}
