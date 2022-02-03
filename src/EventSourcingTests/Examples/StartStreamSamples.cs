using System;
using System.Threading.Tasks;
using Marten;
using Marten.Events;

namespace EventSourcingTests.Examples
{
    public class StartStreamSamples
    {
        public static void configuring_schema()
        {
            #region sample_set_event_store_schema

            var store = DocumentStore.For(opts =>
            {
                opts.Connection("some connection string");

                opts.Events.DatabaseSchemaName = "events";
            });

            #endregion
        }

        public static void configure_stream_identity()
        {
            #region sample_setting_stream_identity

            var store = DocumentStore.For(opts =>
            {
                opts.Connection("some connection string");

                // Override the stream identity to use strings
                opts.Events.StreamIdentity = StreamIdentity.AsString;
            });

            #endregion
        }


        #region sample_start_stream_with_guid_identifier

        public async Task start_stream_with_guid_stream_identifiers(IDocumentSession session)
        {
            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            // Let Marten assign a new Stream Id, and mark the stream with an aggregate type
            // 'Quest'
            var streamId1 = session.Events.StartStream<Quest>(joined, departed).Id;

            // Or pass the aggregate type in without generics
            var streamId2 = session.Events.StartStream(typeof(Quest), joined, departed);

            // Or instead, you tell Marten what the stream id should be
            var userDefinedStreamId = Guid.NewGuid();
            session.Events.StartStream<Quest>(userDefinedStreamId, joined, departed);

            // Or pass the aggregate type in without generics
            session.Events.StartStream(typeof(Quest), userDefinedStreamId, joined, departed);

            // Or forget about the aggregate type whatsoever
            var streamId4 = session.Events.StartStream(joined, departed);

            // Or start with a known stream id and no aggregate type
            session.Events.StartStream(userDefinedStreamId, joined, departed);


            // And persist the new stream of course
            await session.SaveChangesAsync();
        }

        #endregion


        #region sample_append_optimistic_event

        public async Task append_optimistic(IDocumentSession session, Guid streamId, object[] events)
        {
            // This is doing data access, so it's an async method
            await session.Events.AppendOptimistic(streamId, events);

            // Assume that there is other work happening right here...

            await session.SaveChangesAsync();
        }

        #endregion

        #region sample_append_exclusive_events

        public async Task append_exclusive(IDocumentSession session, Guid streamId)
        {
            // You *could* pass in events here too, but doing this establishes a transaction
            // lock on the stream.
            await session.Events.AppendExclusive(streamId);

            var events = determineNewEvents(streamId);

            // The next call can just be Append()
            session.Events.Append(streamId, events);

            // This will commit the unit of work and release the
            // lock on the event stream
            await session.SaveChangesAsync();
        }

        #endregion

        private object[] determineNewEvents(Guid streamId)
        {
            throw new NotImplementedException();
        }
    }
}
