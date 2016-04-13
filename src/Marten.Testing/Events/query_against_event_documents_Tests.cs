using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class query_against_event_documents_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        private MembersJoined joined1 = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
        private MembersDeparted departed1 = new MembersDeparted { Members = new[] { "Thom" } };

        private MembersJoined joined2 = new MembersJoined { Members = new string[] { "Nynaeve", "Egwene" } };
        private MembersDeparted departed2 = new MembersDeparted { Members = new[] { "Matt" } };


        [Fact]
        public void can_query_against_event_type()
        {
            throw new NotImplementedException("need to introduce IDocumentSession.Events.Query<T>()");

            theSession.Events.StartStream<Quest>(joined1, departed1);
            theSession.Events.StartStream<Quest>(joined2, departed2);

            theSession.SaveChanges();

            theSession.Query<MembersJoined>().Count().ShouldBe(2);
            theSession.Query<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
                .OrderBy(x => x)
                .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

            theSession.Query<MembersDeparted>().Where(x => x.Members.Contains("Matt"))
                .Single().Id.ShouldBe(departed2.Id);
        }

        [Fact]
        public void can_load_event_doc_by_id()
        {
            throw new NotImplementedException("need to introduce IDocumentSession.Events.Query<T>()");

            theSession.Events.StartStream<Quest>(joined1, departed1);
            theSession.Events.StartStream<Quest>(joined2, departed2);

            theSession.SaveChanges();

            theSession.Load<MembersJoined>(joined1.Id).ShouldNotBeNull();

            using (var session = theStore.QuerySession())
            {
                session.Load<MembersJoined>(joined1.Id).ShouldNotBeNull();
            }
        }


        [Fact]
        public async Task can_load_event_doc_by_id_async()
        {
            throw new NotImplementedException("need to introduce IDocumentSession.Events.Query<T>()");

            theSession.Events.StartStream<Quest>(joined1, departed1);
            theSession.Events.StartStream<Quest>(joined2, departed2);

            theSession.SaveChanges();

            theSession.Load<MembersJoined>(joined1.Id).ShouldNotBeNull();

            using (var session = theStore.QuerySession())
            {
                (await session.LoadAsync<MembersJoined>(joined1.Id)).ShouldNotBeNull();
            }
        }

        [Fact]
        public void will_not_blow_up_if_searching_for_events_before_event_store_is_warmed_up()
        {
            theSession.Query<MembersJoined>().Any().ShouldBeFalse();
        }
    }
}