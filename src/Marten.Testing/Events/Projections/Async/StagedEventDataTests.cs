using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Events.Projections.Async;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections.Async
{
    public class StagedEventDataTests : IntegratedFixture
    {
        private readonly StagedEventOptions theOptions = new StagedEventOptions {Name = "something"};

        public StagedEventDataTests()
        {
            theStore.Schema.EnsureStorageExists(typeof(EventStream));

            
        }

        [Fact]
        public async Task can_get_last_event_progression_on_initial_check()
        {
            var factory = new ConnectionSource();

            using (var data = new StagedEventData(theOptions, factory, new EventGraph(new StoreOptions()), new JilSerializer()))
            {
                var lastEncountered = await data.LastEventProgression();

                lastEncountered.ShouldBe(0);
            }
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

            using (var data = new StagedEventData(theOptions, factory, new EventGraph(new StoreOptions()), new JilSerializer()) )
            {
                var lastEncountered = await data.LastEventProgression();

                lastEncountered.ShouldBe(121);
            }

        }

        [Fact]
        public async Task can_register_progress_initial()
        {
            using (var data = new StagedEventData(theOptions, new ConnectionSource(), new EventGraph(new StoreOptions()), new JilSerializer()) )
            {
                await data.RegisterProgress(111);

                var lastEncountered = await data.LastEventProgression();

                lastEncountered.ShouldBe(111);
            }
        }

        [Fact]
        public async Task can_register_subsequent_progress()
        {
            using (var data = new StagedEventData(theOptions, new ConnectionSource(), new EventGraph(new StoreOptions()), new JilSerializer()))
            {
                await data.RegisterProgress(111);
                await data.RegisterProgress(211);

                var lastEncountered = await data.LastEventProgression();

                lastEncountered.ShouldBe(211);
            }
        }

        [Fact]
        public async Task smoke_test_able_to_fetch_a_page_of_events()
        {
            var list = new List<MembersJoined>();

            for (int i = 0; i < 500; i++)
            {
                list.Add(new MembersJoined {Day = i, Location = Guid.NewGuid().ToString(), Members = new string[] {Guid.NewGuid().ToString()}});
            }

            using (var session = theStore.LightweightSession())
            {
                session.Events.Append(Guid.NewGuid(), list.ToArray());
                await session.SaveChangesAsync();
            }

            using (var data = new StagedEventData(theOptions, new ConnectionSource(), theStore.Schema.Events.As<EventGraph>(), new JilSerializer()))
            {
                var events = await data.FetchNextPage();

                events.Count.ShouldBe(theOptions.PageSize);
                events.Each(x => x.ShouldBeOfType<Event<MembersJoined>>());
            }
        }
    }
}