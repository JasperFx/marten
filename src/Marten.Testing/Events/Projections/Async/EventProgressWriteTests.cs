using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Events.Projections.Async;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections.Async
{
    public class EventProgressWriteTests : IntegratedFixture
    {
        public EventProgressWriteTests()
        {
            theStore.Schema.EnsureStorageExists(typeof(EventStream));
        }


        [Fact]
        public async Task can_register_progress_initial()
        {
            using (var session = theStore.OpenSession())
            {
                session.QueueOperation(new EventProgressWrite(theStore.Schema.Events.As<EventGraph>(), "summary", 111));
                session.SaveChanges();


                var last =
                    session.Connection.CreateCommand(
                        "select last_seq_id from mt_event_progression where name = 'summary'")
                        .ExecuteScalar().As<long>();

                last.ShouldBe(111);
            }
        }

        [Fact]
        public async Task can_register_subsequent_progress()
        {
            using (var session = theStore.OpenSession())
            {
                session.QueueOperation(new EventProgressWrite(theStore.Schema.Events.As<EventGraph>(), "summary", 111));
                session.SaveChanges();

                session.QueueOperation(new EventProgressWrite(theStore.Schema.Events.As<EventGraph>(), "summary", 222));
                session.SaveChanges();

                var last =
                    session.Connection.CreateCommand(
                        "select last_seq_id from mt_event_progression where name = 'summary'")
                        .ExecuteScalar().As<long>();

                last.ShouldBe(222);
            }
        }

    }
}