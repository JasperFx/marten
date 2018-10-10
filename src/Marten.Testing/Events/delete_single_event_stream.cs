using System;
using System.Linq;
using Marten.Events;
using Marten.Storage;
using Xunit;

namespace Marten.Testing.Events
{
    public class delete_single_event_stream : IntegratedFixture
    {
        [Theory]
        [InlineData(TenancyStyle.Single)]
        [InlineData(TenancyStyle.Conjoined)]
        public void delete_stream_by_guid_id(TenancyStyle tenancyStyle)
        {
            StoreOptions(_ => _.Events.TenancyStyle = tenancyStyle);

            var stream1 = Guid.NewGuid();
            var stream2 = Guid.NewGuid();

            using (var session = theStore.LightweightSession())
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.Append(stream1, joined, departed);

                var joined2 = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed2 = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.Append(stream2, joined2, departed2);

                session.SaveChanges();
            }

            theStore.Advanced.Clean.DeleteSingleEventStream(stream1);

            using (var session = theStore.LightweightSession())
            {
                session.Events.QueryAllRawEvents().ToList().All(x => x.StreamId == stream2)
                    .ShouldBeTrue();
            }
        }

        [Theory]
        [InlineData(TenancyStyle.Single)]
        [InlineData(TenancyStyle.Conjoined)]
        public void delete_stream_by_string_key(TenancyStyle tenancyStyle)
        {
            StoreOptions(_ =>
            {
                _.Events.StreamIdentity = StreamIdentity.AsString;
                _.Events.TenancyStyle = tenancyStyle;
            });

            var stream1 = "one";
            var stream2 = "two";

            using (var session = theStore.LightweightSession())
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.Append(stream1, joined, departed);

                var joined2 = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed2 = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.Append(stream2, joined2, departed2);

                session.SaveChanges();
            }

            theStore.Advanced.Clean.DeleteSingleEventStream(stream1);

            using (var session = theStore.LightweightSession())
            {
                session.Events.QueryAllRawEvents().ToList().All(x => x.StreamKey == stream2)
                    .ShouldBeTrue();
            }
        }
    }
}