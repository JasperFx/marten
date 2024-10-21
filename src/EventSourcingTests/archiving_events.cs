using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using Marten;
using Marten.Events;
using Marten.Events.Archiving;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests;

public class archiving_events: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public archiving_events(ITestOutputHelper output)
    {
        _output = output;
    }

    #region sample_archive_stream_usage

    public async Task SampleArchive(IDocumentSession session, string streamId)
    {
        session.Events.ArchiveStream(streamId);
        await session.SaveChangesAsync();
    }

    #endregion

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task archive_stream_by_guid(bool usePartitioning)
    {
        StoreOptions(opts => opts.Events.UseArchivedStreamPartitioning = usePartitioning);

        var stream = Guid.NewGuid();

        theSession.Events.StartStream(stream, new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        theSession.Logger = new TestOutputMartenLogger(_output);

        var stream1 = await theSession.Events.FetchStreamStateAsync(stream);
        stream1.IsArchived.ShouldBeFalse();

        var isArchived = await theSession.Connection
            .CreateCommand($"select is_archived from {SchemaName}.mt_events where stream_id = :stream")
            .With("stream", stream).FetchListAsync<bool>();

        // None of the events should be archived
        isArchived.All(x => !x).ShouldBeTrue();

        theSession.Events.ArchiveStream(stream);
        await theSession.SaveChangesAsync();

        var stream2 = await theSession.Events.FetchStreamStateAsync(stream);
        stream2.IsArchived.ShouldBeTrue();

        isArchived = await theSession.Connection
            .CreateCommand($"select is_archived from {SchemaName}.mt_events where stream_id = :stream")
            .With("stream", stream).FetchListAsync<bool>();

        // All of the events should be archived
        isArchived.All(x => x).ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task archive_stream_by_guid_when_tenanted(bool usePartitioning)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = usePartitioning;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        });

        var stream = Guid.NewGuid();

        var session = theStore.LightweightSession("one");
        session.Events.StartStream(stream, new AEvent(), new BEvent(), new CEvent());
        await session.SaveChangesAsync();

        session.Logger = new TestOutputMartenLogger(_output);

        var stream1 = await session.Events.FetchStreamStateAsync(stream);
        stream1.IsArchived.ShouldBeFalse();

        var isArchived = await session.Connection
            .CreateCommand($"select is_archived from {SchemaName}.mt_events where stream_id = :stream")
            .With("stream", stream).FetchListAsync<bool>();

        // None of the events should be archived
        isArchived.All(x => !x).ShouldBeTrue();

        session.Events.ArchiveStream(stream);
        await session.SaveChangesAsync();

        var stream2 = await session.Events.FetchStreamStateAsync(stream);
        stream2.IsArchived.ShouldBeTrue();

        isArchived = await session.Connection
            .CreateCommand($"select is_archived from {SchemaName}.mt_events where stream_id = :stream")
            .With("stream", stream).FetchListAsync<bool>();

        // All of the events should be archived
        isArchived.All(x => x).ShouldBeTrue();
    }

    [Fact]
    public async Task fetch_stream_filters_out_archived_events()
    {
        var stream = Guid.NewGuid();

        theSession.Events.StartStream(stream, new AEvent(), new BEvent(), new CEvent(), new CEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        await theSession.Connection.CreateCommand($"update {SchemaName}.mt_events set is_archived = TRUE where version < 2")
            .ExecuteNonQueryAsync();

        var events = await theSession.Events.FetchStreamAsync(stream);

        events.All(x => x.Version >= 2).ShouldBeTrue();

    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task archive_stream_by_string(bool usePartitioning)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = usePartitioning;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var stream = Guid.NewGuid().ToString();

        theSession.Events.StartStream(stream, new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        var stream1 = await theSession.Events.FetchStreamStateAsync(stream);
        stream1.IsArchived.ShouldBeFalse();

        var isArchived = await theSession.Connection
            .CreateCommand($"select is_archived from {SchemaName}.mt_events where stream_id = :stream")
            .With("stream", stream).FetchListAsync<bool>();

        // None of the events should be archived
        isArchived.All(x => !x).ShouldBeTrue();

        theSession.Events.ArchiveStream(stream);
        await theSession.SaveChangesAsync();

        var stream2 = await theSession.Events.FetchStreamStateAsync(stream);
        stream2.IsArchived.ShouldBeTrue();

        isArchived = await theSession.Connection
            .CreateCommand($"select is_archived from {SchemaName}.mt_events where stream_id = :stream")
            .With("stream", stream).FetchListAsync<bool>();

        // All of the events should be archived
        isArchived.All(x => x).ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task query_by_events_filters_out_archived_events_by_default(bool usePartitioning)
    {
        StoreOptions(opts => opts.Events.UseArchivedStreamPartitioning = usePartitioning);

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task query_by_events_and_explicitly_search_for_archived_events(bool usePartitioning)
    {
        StoreOptions(opts => opts.Events.UseArchivedStreamPartitioning = usePartitioning);

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();
        var stream3 = Guid.NewGuid();

        theSession.Events.Append(stream1, new AEvent(), new BEvent(), new CEvent());
        theSession.Events.Append(stream2, new AEvent(), new BEvent(), new CEvent());
        theSession.Events.Append(stream3, new AEvent(), new BEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        theSession.Logger = new TestOutputMartenLogger(_output);

        theSession.Events.ArchiveStream(stream2);
        await theSession.SaveChangesAsync();

        #region sample_querying_for_archived_events

        var events = await theSession.Events
            .QueryAllRawEvents()
            .Where(x => x.IsArchived)
            .ToListAsync();

        #endregion

        events.Count.ShouldBe(3);
        events.All(x => x.StreamId == stream2).ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task query_by_events_and_explicitly_search_for_maybe_archived_events(bool usePartitioning)
    {
        StoreOptions(opts => opts.Events.UseArchivedStreamPartitioning = usePartitioning);

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

        #region sample_query_for_maybe_archived_events

        var events = await theSession.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived()).ToListAsync();

        #endregion

        events.Count(x => x.IsArchived).ShouldBe(3);
        events.Count(x => !x.IsArchived).ShouldBe(6);

        events.Count.ShouldBe(9);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task query_by_a_specific_event_filters_out_archived_events_by_default(bool usePartitioning)
    {
        StoreOptions(opts => opts.Events.UseArchivedStreamPartitioning = usePartitioning);

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

        #region sample_replacing_logger_per_session

        // We frequently use this special marten logger per
        // session to pipe Marten logging to the xUnit.Net output
        theSession.Logger = new TestOutputMartenLogger(_output);

        #endregion

        var events = await theSession.Events.QueryRawEventDataOnly<AEvent>().ToListAsync();

        events.Count.ShouldBe(2);
        events.All(x => x.Tracker != aEvent2.Tracker).ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task prevent_append_operation_for_archived_stream_on_sync_commit(bool usePartitioning)
    {
        StoreOptions(opts => opts.Events.UseArchivedStreamPartitioning = usePartitioning);

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new AEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.ArchiveStream(streamId);
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new BEvent());
        var thrownException = await Should.ThrowAsync<InvalidStreamOperationException>( async () =>
        {
            await theSession.SaveChangesAsync();
        });

        thrownException.Message.ShouldBe($"Attempted to append event to archived stream with Id '{streamId}'.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task prevent_append_operation_for_archived_stream_on_async_commit(bool usePartitioning)
    {
        StoreOptions(opts => opts.Events.UseArchivedStreamPartitioning = usePartitioning);

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new AEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.ArchiveStream(streamId);
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new BEvent());
        var thrownException = Should.Throw<InvalidStreamOperationException>( async () =>
        {
            await theSession.SaveChangesAsync();
        });
        thrownException.Message.ShouldBe($"Attempted to append event to archived stream with Id '{streamId}'.");
    }
}
