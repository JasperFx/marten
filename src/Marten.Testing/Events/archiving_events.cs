using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Archiving;
using Marten.Testing.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events
{
    public class archiving_events: IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public archiving_events(DefaultStoreFixture fixture, ITestOutputHelper output): base(fixture)
        {
            _output = output;
        }

        [Fact]
        public async Task archive_stream_by_guid()
        {
            var stream = Guid.NewGuid();

            theSession.Events.StartStream(stream, new AEvent(), new BEvent(), new CEvent());
            await theSession.SaveChangesAsync();

            var stream1 = await theSession.Events.FetchStreamStateAsync(stream);
            stream1.IsArchived.ShouldBeFalse();

            var isArchived = await theSession.Connection
                .CreateCommand("select is_archived from mt_events where stream_id = :stream")
                .With("stream", stream).FetchList<bool>();

            // None of the events should be archived
            isArchived.All(x => !x).ShouldBeTrue();

            theSession.Events.ArchiveStream(stream);
            await theSession.SaveChangesAsync();

            var stream2 = await theSession.Events.FetchStreamStateAsync(stream);
            stream2.IsArchived.ShouldBeTrue();

            isArchived = await theSession.Connection
                .CreateCommand("select is_archived from mt_events where stream_id = :stream")
                .With("stream", stream).FetchList<bool>();

            // All of the events should be archived
            isArchived.All(x => x).ShouldBeTrue();
        }

        [Fact]
        public async Task fetch_stream_filters_out_archived_events()
        {
            var stream = Guid.NewGuid();

            theSession.Events.StartStream(stream, new AEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent());
            await theSession.SaveChangesAsync();

            await theSession.Connection.CreateCommand("update mt_events set is_archived = TRUE where version < 2")
                .ExecuteNonQueryAsync();

            var events = await theSession.Events.FetchStreamAsync(stream);

            events.All(x => x.Version >= 2).ShouldBeTrue();

        }

        [Fact] // TODO -- incorporate this in the events_workflow_specs maybe?
        // find some way to share the ByString DocumentStore
        public async Task archive_stream_by_string()
        {
            StoreOptions(opts =>
            {
                opts.Events.StreamIdentity = StreamIdentity.AsString;
                opts.DatabaseSchemaName = "string_events";
            });

            var stream = Guid.NewGuid().ToString();

            theSession.Events.StartStream(stream, new AEvent(), new BEvent(), new CEvent());
            await theSession.SaveChangesAsync();

            var stream1 = await theSession.Events.FetchStreamStateAsync(stream);
            stream1.IsArchived.ShouldBeFalse();

            var isArchived = await theSession.Connection
                .CreateCommand("select is_archived from string_events.mt_events where stream_id = :stream")
                .With("stream", stream).FetchList<bool>();

            // None of the events should be archived
            isArchived.All(x => !x).ShouldBeTrue();

            theSession.Events.ArchiveStream(stream);
            await theSession.SaveChangesAsync();

            var stream2 = await theSession.Events.FetchStreamStateAsync(stream);
            stream2.IsArchived.ShouldBeTrue();

            isArchived = await theSession.Connection
                .CreateCommand("select is_archived from string_events.mt_events where stream_id = :stream")
                .With("stream", stream).FetchList<bool>();

            // All of the events should be archived
            isArchived.All(x => x).ShouldBeTrue();
        }

        [Fact]
        public async Task query_by_events_filters_out_archived_events_by_default()
        {
            await theStore.Advanced.Clean.DeleteAllEventDataAsync();

            var stream1 = Guid.NewGuid();
            var stream2 = Guid.NewGuid();
            var stream3 = Guid.NewGuid();

            theSession.Events.Append(stream1, new AEvent(), new BEvent(), new CEvent());
            theSession.Events.Append(stream2, new AEvent(), new BEvent(), new CEvent());
            theSession.Events.Append(stream3, new AEvent(), new BEvent(), new CEvent());

            await theSession.SaveChangesAsync();

            theSession.Events.ArchiveStream(stream2);
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.QueryAllRawEvents().ToListAsync();

            events.Count.ShouldBe(6);
            events.All(x => x.StreamId != stream2).ShouldBeTrue();
        }

        [Fact]
        public async Task query_by_events_and_explicitly_search_for_archived_events()
        {
            await theStore.Advanced.Clean.DeleteAllEventDataAsync();

            var stream1 = Guid.NewGuid();
            var stream2 = Guid.NewGuid();
            var stream3 = Guid.NewGuid();

            theSession.Events.Append(stream1, new AEvent(), new BEvent(), new CEvent());
            theSession.Events.Append(stream2, new AEvent(), new BEvent(), new CEvent());
            theSession.Events.Append(stream3, new AEvent(), new BEvent(), new CEvent());

            await theSession.SaveChangesAsync();

            theSession.Events.ArchiveStream(stream2);
            await theSession.SaveChangesAsync();

            var events = await theSession.Events
                .QueryAllRawEvents()
                .Where(x => x.IsArchived)
                .ToListAsync();

            events.Count.ShouldBe(3);
            events.All(x => x.StreamId == stream2).ShouldBeTrue();
        }

        [Fact]
        public async Task query_by_events_and_explicitly_search_for_maybe_archived_events()
        {
            await theStore.Advanced.Clean.DeleteAllEventDataAsync();

            var stream1 = Guid.NewGuid();
            var stream2 = Guid.NewGuid();
            var stream3 = Guid.NewGuid();

            theSession.Events.Append(stream1, new AEvent(), new BEvent(), new CEvent());
            theSession.Events.Append(stream2, new AEvent(), new BEvent(), new CEvent());
            theSession.Events.Append(stream3, new AEvent(), new BEvent(), new CEvent());

            await theSession.SaveChangesAsync();

            theSession.Events.ArchiveStream(stream2);
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.QueryAllRawEvents().Where(x => x.MaybeArchived()).ToListAsync();

            events.Count(x => x.IsArchived).ShouldBe(3);
            events.Count(x => !x.IsArchived).ShouldBe(6);

            events.Count.ShouldBe(9);
        }

        [Fact]
        public async Task query_by_a_specific_event_filters_out_archived_events_by_default()
        {
            await theStore.Advanced.Clean.DeleteAllEventDataAsync();

            var stream1 = Guid.NewGuid();
            var stream2 = Guid.NewGuid();
            var stream3 = Guid.NewGuid();

            var aEvent1 = new AEvent();
            var aEvent2 = new AEvent();
            var aEvent3 = new AEvent();
            theSession.Events.Append(stream1, aEvent1, new BEvent(), new CEvent());
            theSession.Events.Append(stream2, aEvent2, new BEvent(), new CEvent());
            theSession.Events.Append(stream3, aEvent3, new BEvent(), new CEvent());

            await theSession.SaveChangesAsync();

            theSession.Events.ArchiveStream(stream2);
            await theSession.SaveChangesAsync();

            theSession.Logger = new TestOutputMartenLogger(_output);

            var events = await theSession.Events.QueryRawEventDataOnly<AEvent>().ToListAsync();

            events.Count.ShouldBe(2);
            events.All(x => x.Tracker != aEvent2.Tracker).ShouldBeTrue();
        }

    }
}
