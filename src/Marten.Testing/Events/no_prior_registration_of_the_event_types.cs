using System;
using System.Threading.Tasks;
using Marten.Events;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class no_prior_registration_of_the_event_types : IntegratedFixture
    {
        [Fact]
        public void can_fetch_sync_with_guids()
        {
            var stream = Guid.NewGuid();
            using (var session = theStore.OpenSession())
            {
                session.Events.StartStream(stream, new MembersJoined(), new MembersDeparted());
                session.SaveChanges();
            }

            // Needs to be an isolated, separate document store to the same db
            using (var store = DocumentStore.For(ConnectionSource.ConnectionString))
            {
                using (var session = store.OpenSession())
                {
                    var events = session.Events.FetchStream(stream);
                    events[0].Data.ShouldBeOfType<MembersJoined>();
                    events[1].Data.ShouldBeOfType<MembersDeparted>();
                }
            }
        }

        [Fact]
        public void can_fetch_sync_with_strings()
        {
            StoreOptions(_ => _.Events.StreamIdentity = StreamIdentity.AsString);

            var stream = "Something";
            using (var session = theStore.OpenSession())
            {
                session.Events.StartStream(stream, new MembersJoined(), new MembersDeparted());
                session.SaveChanges();
            }

            // Needs to be an isolated, separate document store to the same db
            using (var store = DocumentStore.For(_ =>
            {
                _.Events.StreamIdentity = StreamIdentity.AsString;
                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                using (var session = store.OpenSession())
                {
                    var events = session.Events.FetchStream(stream);
                    events[0].Data.ShouldBeOfType<MembersJoined>();
                    events[1].Data.ShouldBeOfType<MembersDeparted>();
                }
            }
        }

        [Fact]
        public async Task can_fetch_async_with_guids()
        {
            var stream = Guid.NewGuid();
            using (var session = theStore.OpenSession())
            {
                session.Events.StartStream(stream, new MembersJoined(), new MembersDeparted());
                await session.SaveChangesAsync();
            }

            // Needs to be an isolated, separate document store to the same db
            using (var store = DocumentStore.For(ConnectionSource.ConnectionString))
            {
                using (var session = store.OpenSession())
                {
                    var events = await session.Events.FetchStreamAsync(stream);
                    events[0].Data.ShouldBeOfType<MembersJoined>();
                    events[1].Data.ShouldBeOfType<MembersDeparted>();
                }
            }
        }

        [Fact]
        public async Task can_fetch_async_with_strings()
        {
            StoreOptions(_ => _.Events.StreamIdentity = StreamIdentity.AsString);

            var stream = "Something";
            using (var session = theStore.OpenSession())
            {
                session.Events.StartStream(stream, new MembersJoined(), new MembersDeparted());
                await session.SaveChangesAsync();
            }

            // Needs to be an isolated, separate document store to the same db
            using (var store = DocumentStore.For(_ =>
            {
                _.Events.StreamIdentity = StreamIdentity.AsString;
                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                using (var session = store.OpenSession())
                {
                    var events = await session.Events.FetchStreamAsync(stream);
                    events[0].Data.ShouldBeOfType<MembersJoined>();
                    events[1].Data.ShouldBeOfType<MembersDeparted>();
                }
            }
        }
    }
}