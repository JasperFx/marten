using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Util;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events
{
    public class end_to_end_event_capture_and_fetching_the_stream_with_rolling_buffer_Tests
    {
        private readonly ITestOutputHelper _output;

        public end_to_end_event_capture_and_fetching_the_stream_with_rolling_buffer_Tests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_in_same_schema()
        {
            var store = InitStore("public");


            Guid stream = Guid.NewGuid();
            using (var session = store.OpenSession(DocumentTracking.IdentityOnly))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.StartStream<Quest>(stream, joined, departed);
                session.SaveChanges();

                var streamEvents = session.Events.FetchStream(stream);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);
            }

            var events = new List<Guid>();

            using (var conn = store.Advanced.OpenConnection())
            {
                conn.Execute(cmd =>
                {
                    cmd.CommandText = "select event_id, stream_id from public.mt_rolling_buffer where reference_count = 1 and stream_id = :stream";
                    cmd.With("stream", stream);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var evt = reader.GetGuid(0);
                            events.Add(evt);

                            reader.GetGuid(1).ShouldBe(stream);
                        }
                    }
                });
            }

            events.Count.ShouldBe(2);


        }


        [Fact]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_in_another_database_schema()
        {
            var store = InitStore("event_store");


            Guid stream = Guid.NewGuid();
            using (var session = store.OpenSession(DocumentTracking.IdentityOnly))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.StartStream<Quest>(stream, joined, departed);
                session.SaveChanges();

                var streamEvents = session.Events.FetchStream(stream);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);
            }

            var events = new List<Guid>();

            using (var conn = store.Advanced.OpenConnection())
            {
                conn.Execute(cmd =>
                {
                    cmd.CommandText = "select event_id, stream_id from event_store.mt_rolling_buffer where reference_count = 1 and stream_id = :stream";
                    cmd.With("stream", stream);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var evt = reader.GetGuid(0);
                            events.Add(evt);

                            reader.GetGuid(1).ShouldBe(stream);
                        }
                    }
                });
            }

            events.Count.ShouldBe(2);


        }


        private static DocumentStore InitStore(string databascSchema = null)
        {
            var store = DocumentStore.For(_ =>
            {
                if (databascSchema != null)
                {
                    _.Events.DatabaseSchemaName = databascSchema;
                }

                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Connection(ConnectionSource.ConnectionString);

                _.Events.AddEventType(typeof(MembersJoined));
                _.Events.AddEventType(typeof(MembersDeparted));

                _.Events.AsyncProjectionsEnabled = true;
                
            });

            store.EventStore.InitializeEventStoreInDatabase();

            return store;
        }
    }
}