using System;
using System.Collections.Generic;
using Marten.Schema;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class insert_to_rolling_buffer_table : IntegratedFixture
    {
        [Fact]
        public void can_insert_into_the_rolling_buffer_table()
        {
            StoreOptions(_ => _.Events.AsyncProjectionsEnabled = true);

            theStore.EventStore.InitializeEventStoreInDatabase();

            var stream = Guid.NewGuid();
            var event1 = Guid.NewGuid();
            var event2 = Guid.NewGuid();
            var event3 = Guid.NewGuid();

            using (var conn = theStore.Advanced.OpenConnection())
            {
                conn.Execute(c =>
                {
                    c.CallsSproc(new FunctionName("public", "mt_append_rolling_buffer"))
                        .With("event", event1).With("stream", stream);

                    c.ExecuteNonQuery();

                    c.Parameters["event"].Value = event2;
                    c.ExecuteNonQuery();

                    c.Parameters["event"].Value = event3;
                    c.ExecuteNonQuery();

                });

                var events = new List<Guid>();

                conn.Execute(cmd =>
                {
                    cmd.CommandText = "select event_id, stream_id from mt_rolling_buffer where reference_count = 1";

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

                events.Count.ShouldBe(3);

                events.ShouldContain(event1);
                events.ShouldContain(event2);
                events.ShouldContain(event3);
            }


        }
    }
}