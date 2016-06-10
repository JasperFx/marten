using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class capturing_event_versions_on_existing_streams_after_append : IntegratedFixture
    {
        [Fact]
        public void running_synchronously()
        {
            Guid streamId = Guid.NewGuid();

            using (var session = theStore.OpenSession())
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.StartStream<Quest>(streamId, joined, departed);
                session.SaveChanges();

                var events = session.LastCommit.GetEvents().ToArray();
                events.Select(x => x.Version)
                    .ShouldHaveTheSameElementsAs(1, 2);

                events.Each(x => x.Sequence.ShouldBeGreaterThan(0L));

                events.Select(x => x.Sequence).Distinct().Count().ShouldBe(2);
            }

            using (var session = theStore.OpenSession())
            {
                var joined2 = new MembersJoined { Members = new[] { "Egwene" } };
                var departed2 = new MembersDeparted { Members = new[] { "Perrin" } };

                session.Events.Append(streamId, joined2, departed2);
                session.SaveChanges();

                session.LastCommit.GetEvents().Select(x => x.Version)
                    .ShouldHaveTheSameElementsAs(3, 4);
            }

            using (var session = theStore.OpenSession())
            {
                var joined3 = new MembersJoined { Members = new[] { "Egwene" } };
                var departed3 = new MembersDeparted { Members = new[] { "Perrin" } };

                session.Events.Append(streamId, joined3, departed3);
                session.SaveChanges();

                session.LastCommit.GetEvents().Select(x => x.Version)
                    .ShouldHaveTheSameElementsAs(5, 6);
            }
        }

        [Fact]
        public async Task running_asynchronously()
        {
            Guid streamId = Guid.NewGuid();

            using (var session = theStore.OpenSession())
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.StartStream<Quest>(streamId, joined, departed);
                await session.SaveChangesAsync();

                session.LastCommit.GetEvents().Select(x => x.Version)
                    .ShouldHaveTheSameElementsAs(1, 2);
            }

            using (var session = theStore.OpenSession())
            {
                var joined2 = new MembersJoined { Members = new[] { "Egwene" } };
                var departed2 = new MembersDeparted { Members = new[] { "Perrin" } };

                session.Events.Append(streamId, joined2, departed2);
                await session.SaveChangesAsync();

                session.LastCommit.GetEvents().Select(x => x.Version)
                    .ShouldHaveTheSameElementsAs(3, 4);
            }

            using (var session = theStore.OpenSession())
            {
                var joined3 = new MembersJoined { Members = new[] { "Egwene" } };
                var departed3 = new MembersDeparted { Members = new[] { "Perrin" } };

                session.Events.Append(streamId, joined3, departed3);
                await session.SaveChangesAsync();

                var events = session.LastCommit.GetEvents().ToArray();
                events.Select(x => x.Version)
                    .ShouldHaveTheSameElementsAs(5, 6);

                events.Each(x => x.Sequence.ShouldBeGreaterThan(0L));

                events.Select(x => x.Sequence).Distinct().Count().ShouldBe(2);
            }
        }
    }
}