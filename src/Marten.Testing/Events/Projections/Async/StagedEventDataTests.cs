using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly StagedEventOptions theOptions = new StagedEventOptions {Name = "something", EventTypeNames = new [] {"members_joined"}};

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
                var lastEncountered = await data.LastEventProgression().ConfigureAwait(false);

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

                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);

                }
            }

            using (var data = new StagedEventData(theOptions, factory, new EventGraph(new StoreOptions()), new JilSerializer()) )
            {
                var lastEncountered = await data.LastEventProgression().ConfigureAwait(false);

                lastEncountered.ShouldBe(121);
            }

        }

        [Fact]
        public async Task can_register_progress_initial()
        {
            using (var data = new StagedEventData(theOptions, new ConnectionSource(), new EventGraph(new StoreOptions()), new JilSerializer()) )
            {
                await data.RegisterProgress(111).ConfigureAwait(false);

                var lastEncountered = await data.LastEventProgression().ConfigureAwait(false);

                lastEncountered.ShouldBe(111);
            }
        }

        [Fact]
        public async Task can_register_subsequent_progress()
        {
            using (var data = new StagedEventData(theOptions, new ConnectionSource(), new EventGraph(new StoreOptions()), new JilSerializer()))
            {
                await data.RegisterProgress(111).ConfigureAwait(false);
                await data.RegisterProgress(211).ConfigureAwait(false);

                var lastEncountered = await data.LastEventProgression().ConfigureAwait(false);

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
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var data = new StagedEventData(theOptions, new ConnectionSource(), theStore.Schema.Events.As<EventGraph>(), new JilSerializer()))
            {
                var page = await data.FetchNextPage(0).ConfigureAwait(false);

                page.From.ShouldBe(0);
                page.To.ShouldBe(data.Options.PageSize);

                page.Streams.SelectMany(x => x.Events).Count().ShouldBe(100);
            }
        }

        [Fact]
        public async Task filters_by_event_type_name()
        {
            using (var session = theStore.OpenSession())
            {
                for (int i = 0; i < 20; i++)
                {
                    var joined = new MembersJoined
                    {
                        Day = i,
                        Location = Guid.NewGuid().ToString(),
                        Members = new string[] { Guid.NewGuid().ToString() }
                    };

                    var departed = new MembersDeparted { Day = i, Location = Guid.NewGuid().ToString(), Members = new[] { Guid.NewGuid().ToString() } };

                    var reached = new ArrivedAtLocation { Day = i, Location = Guid.NewGuid().ToString() };

                    session.Events.Append(Guid.NewGuid(), joined, departed, reached);
                }

                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            var events = theStore.Schema.Events.As<EventGraph>();
            theOptions.EventTypeNames = new[]
            {
                events.EventMappingFor<MembersDeparted>().EventTypeName,
                events.EventMappingFor<ArrivedAtLocation>().EventTypeName
            };

            using (var data = new StagedEventData(theOptions, new ConnectionSource(), events, new JilSerializer()))
            {
                var page = await data.FetchNextPage(0).ConfigureAwait(false);

                var all = page.Streams.SelectMany(x => x.Events).ToArray();

                all.OfType<Event<MembersJoined>>().Any().ShouldBeFalse();
                all.OfType<Event<MembersDeparted>>().Any().ShouldBeTrue();
                all.OfType<Event<ArrivedAtLocation>>().Any().ShouldBeTrue();
            }

        }

        [Fact]
        public async Task correctly_correlates_by_stream()
        {
            var streams = new List<EventStream>();

            using (var session = theStore.LightweightSession())
            {
                for (int i = 0; i < 20; i++)
                {
                    var joined = new MembersJoined
                    {
                        Day = i,
                        Location = Guid.NewGuid().ToString(),
                        Members = new string[] { Guid.NewGuid().ToString() }
                    };

                    var departed = new MembersDeparted { Day = i, Location = Guid.NewGuid().ToString(), Members = new[] { Guid.NewGuid().ToString() } };

                    var reached = new ArrivedAtLocation { Day = i, Location = Guid.NewGuid().ToString() };

                    session.Events.Append(Guid.NewGuid(), joined, departed, reached);
                }

                await session.SaveChangesAsync().ConfigureAwait(false);

                streams.AddRange(session.LastCommit.GetStreams());
            }



            var events = theStore.Schema.Events.As<EventGraph>();
            theOptions.EventTypeNames = new[]
            {
                events.EventMappingFor<MembersJoined>().EventTypeName,
                events.EventMappingFor<MembersDeparted>().EventTypeName,
                events.EventMappingFor<ArrivedAtLocation>().EventTypeName
            };

            using (var data = new StagedEventData(theOptions, new ConnectionSource(), events, new JilSerializer()))
            {
                var page = await data.FetchNextPage(0).ConfigureAwait(false);

                foreach (var stream in page.Streams)
                {
                    var existing = streams.Single(x => x.Id == stream.Id);

                    existing.Events.Select(x => x.Id)
                        .ShouldHaveTheSameElementsAs(stream.Events.Select(x => x.Id));
                }
            }

        }


        [Fact]
        public async Task able_to_page_events()
        {
            var list = new List<MembersJoined>();

            for (int i = 0; i < 500; i++)
            {
                list.Add(new MembersJoined { Day = i, Location = Guid.NewGuid().ToString(), Members = new string[] { Guid.NewGuid().ToString() } });
            }

            using (var session = theStore.LightweightSession())
            {
                session.Events.Append(Guid.NewGuid(), list.ToArray());
                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var data = new StagedEventData(theOptions, new ConnectionSource(), theStore.Schema.Events.As<EventGraph>(), new JilSerializer()))
            {
                var events1 = (await data.FetchNextPage(0).ConfigureAwait(false)).Streams.SelectMany(x => x.Events).ToArray();
                var events2 = (await data.FetchNextPage(100).ConfigureAwait(false)).Streams.SelectMany(x => x.Events).ToArray();
                var events3 = (await data.FetchNextPage(200).ConfigureAwait(false)).Streams.SelectMany(x => x.Events).ToArray();

                events1.Intersect(events2).Any().ShouldBeFalse();
                events1.Intersect(events3).Any().ShouldBeFalse();
                events3.Intersect(events2).Any().ShouldBeFalse();

                events1.Length.ShouldBe(100);
                events2.Length.ShouldBe(100);
                events3.Length.ShouldBe(100);
            }
        }

    }
}