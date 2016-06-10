using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Projections.Async;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections.Async
{
    public class StagedEventDataTests : IntegratedFixture
    {
        public StagedEventDataTests()
        {
            theStore.Schema.EnsureStorageExists(typeof(EventStream));
        }

        [Fact]
        public async Task can_get_last_event_progression_on_initial_check()
        {
            var factory = new ConnectionSource();

            var data = new StagedEventData(factory, new EventGraph(new StoreOptions()), new JilSerializer());

            var lastEncountered = await data.LastEventProgression("something");

            lastEncountered.ShouldBe(0);
        }

        [Fact]
        public async Task can_get_last_event_progression_from_existing_data()
        {
            var factory = new ConnectionSource();

            using (var conn = factory.Create())
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Sql("insert into mt_event_progression (name, last_seq_id) values ('something', 121)");

                    await cmd.ExecuteNonQueryAsync();

                }
            }

            using (var data = new StagedEventData(factory, new EventGraph(new StoreOptions()), new JilSerializer()))
            {
                var lastEncountered = await data.LastEventProgression("something");

                lastEncountered.ShouldBe(121);
            }

        }

        [Fact]
        public async Task can_register_progress_initial()
        {
            using (var data = new StagedEventData(new ConnectionSource(), new EventGraph(new StoreOptions()), new JilSerializer()))
            {
                await data.RegisterProgress("something", 111);

                var lastEncountered = await data.LastEventProgression("something");

                lastEncountered.ShouldBe(111);
            }
        }

        [Fact]
        public async Task can_register_subsequent_progress()
        {
            using (var data = new StagedEventData(new ConnectionSource(), new EventGraph(new StoreOptions()), new JilSerializer()))
            {
                await data.RegisterProgress("something", 111);
                await data.RegisterProgress("something", 211);
                await data.RegisterProgress("else", 333);

                var lastEncountered = await data.LastEventProgression("something");

                lastEncountered.ShouldBe(211);
            }
        }
    }
}