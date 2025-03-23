using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Archiving;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using StronglyTypedIds;
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

    [Fact]
    public async Task capture_archived_event_with_inline_projection_will_archive_the_stream()
    {
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline));

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new DEvent(), new Archived("Complete"));
        await theSession.SaveChangesAsync();

        // All the events should be archived
        var events = await theSession.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamId == streamId).ToListAsync();

        events.All(x => x.IsArchived).ShouldBeTrue();
    }


    [Fact]
    public async Task capture_archived_event_with_inline_custom_projection_will_archive_the_stream()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new SimpleAggregateProjection2(), ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new DEvent(), new Archived("Complete"));
        await theSession.SaveChangesAsync();

        // All the events should be archived
        var events = await theSession.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamId == streamId).ToListAsync();

        events.All(x => x.IsArchived).ShouldBeTrue();
    }


    [Fact]
    public async Task capture_archived_event_with_async_projection_will_archive_the_stream()
    {
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async));

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new DEvent(), new Archived("Complete"));
        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(5.Seconds());

        // All the events should be archived
        var events = await theSession.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamId == streamId).ToListAsync();

        events.All(x => x.IsArchived).ShouldBeTrue();
    }


    [Fact]
    public async Task capture_archived_event_with_inline_projection_will_archive_the_stream_string_identified()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregateAsString>(SnapshotLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new DEvent(), new Archived("Complete"));
        await theSession.SaveChangesAsync();

        // All the events should be archived
        var events = await theSession.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamKey == streamId).ToListAsync();

        events.All(x => x.IsArchived).ShouldBeTrue();
    }

    [Fact]
    public async Task capture_archived_with_delete_event()
    {
        StoreOptions(opts => opts.Projections.Add<SimpleAggregateProjection>(ProjectionLifecycle.Inline));

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new DEvent(), new Deleted(), new Archived("Complete"));
        await theSession.SaveChangesAsync();

        // All the events should be archived
        var events = await theSession.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamId == streamId).ToListAsync();

        events.All(x => x.IsArchived).ShouldBeTrue();

        var doc = await theSession.LoadAsync<SimpleAggregate>(streamId);
        doc.ShouldBeNull();
    }

    [Fact]
    public async Task capture_archived_with_should_delete_event()
    {
        StoreOptions(opts => opts.Projections.Add<SimpleAggregateProjection>(ProjectionLifecycle.Inline));

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new DEvent(), new MaybeDeleted(true), new Archived("Complete"));
        await theSession.SaveChangesAsync();

        // All the events should be archived
        var events = await theSession.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamId == streamId).ToListAsync();

        events.All(x => x.IsArchived).ShouldBeTrue();

        var doc = await theSession.LoadAsync<SimpleAggregate>(streamId);
        doc.ShouldBeNull();
    }

    [Fact]
    public async Task capture_archived_event_with_inline_projection_will_archive_the_stream_guid_wrapped_strong_typed_identifier()
    {
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregateStrongTypedGuid>(SnapshotLifecycle.Inline));

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregateStrongTypedGuid>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new DEvent(), new Archived("Complete"));
        await theSession.SaveChangesAsync();

        // All the events should be archived
        var events = await theSession.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamId == streamId).ToListAsync();

        events.All(x => x.IsArchived).ShouldBeTrue();
    }


    [Fact]
    public async Task capture_archived_event_with_inline_projection_will_archive_the_stream_string_wrapped_strong_typed_identifier()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregateStrongTypedString>(SnapshotLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var streamId = Guid.NewGuid().ToString();

        theSession.Events.StartStream<SimpleAggregateStrongTypedGuid>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new DEvent(), new Archived("Complete"));
        await theSession.SaveChangesAsync();

        // All the events should be archived
        var events = await theSession.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamKey == streamId).ToListAsync();

        events.All(x => x.IsArchived).ShouldBeTrue();
    }

    [Fact]
    public async Task using_with_conjoined_tenancy_and_for_tenant_string_identity()
    {
        const string streamKey = "test-stream";
        const string tenantId = "test-tenant";

        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseArchivedStreamPartitioning = true;
        });

        theSession.ForTenant(tenantId).Events.Append(streamKey, new TestEvent(1), new TestEvent(2));
        await theSession.SaveChangesAsync();

        theSession.ForTenant(tenantId).Events.ArchiveStream(streamKey);
        await theSession.SaveChangesAsync();

        StreamState? state = await theSession.ForTenant(tenantId).Events.FetchStreamStateAsync(streamKey);

        // ASSERT
        state.ShouldNotBeNull();
        state.IsArchived.ShouldBeTrue();
    }

    [Fact]
    public async Task using_with_conjoined_tenancy_and_for_tenant_guid_identity()
    {
        var streamKey = Guid.NewGuid();
        var tenantId = "test-tenant";

        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseArchivedStreamPartitioning = true;
        });

        theSession.ForTenant(tenantId).Events.Append(streamKey, new TestEvent(1), new TestEvent(2));
        await theSession.SaveChangesAsync();

        theSession.ForTenant(tenantId).Events.ArchiveStream(streamKey);
        await theSession.SaveChangesAsync();

        StreamState? state = await theSession.ForTenant(tenantId).Events.FetchStreamStateAsync(streamKey);

        // ASSERT
        state.ShouldNotBeNull();
        state.IsArchived.ShouldBeTrue();
    }

    internal record TestEvent(int Id);
}

public class SimpleAggregateProjection: SingleStreamProjection<SimpleAggregate, Guid>
{
    public SimpleAggregateProjection()
    {
        DeleteEvent<Deleted>();
    }

    public void Apply(SimpleAggregate aggregate, AEvent e) => aggregate.ACount++;

    public bool ShouldDelete(MaybeDeleted e) => e.ShouldDelete;
}

public class SimpleAggregateProjection2: SingleStreamProjection<SimpleAggregate, Guid>
{
    public override SimpleAggregate Evolve(SimpleAggregate snapshot, Guid id, IEvent @event)
    {
        snapshot ??= new SimpleAggregate();

        switch (@event.Data)
        {
            case AEvent _:
                snapshot.ACount++;
                break;

            case BEvent _:
                snapshot.BCount++;
                break;
        }

        return snapshot;
    }
}

public record Deleted;

public record MaybeDeleted(bool ShouldDelete);

[StronglyTypedId(Template.Guid)]
public partial struct GuidId;

public class SimpleAggregateStrongTypedGuid
{
    // This will be the aggregate version
    public int Version { get; set; }

    public GuidId? Id { get; set; }

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
    public int ECount { get; set; }

    public void Apply(AEvent _)
    {
        ACount++;
    }

    public void Apply(BEvent _)
    {
        BCount++;
    }

    public void Apply(CEvent _)
    {
        CCount++;
    }

    public void Apply(DEvent _)
    {
        DCount++;
    }

    public void Apply(EEvent _)
    {
        ECount++;
    }

}

[StronglyTypedId(Template.String)]
public partial struct StringId;

public class SimpleAggregateStrongTypedString
{
    // This will be the aggregate version
    public int Version { get; set; }

    public StringId? Id { get; set; }

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
    public int ECount { get; set; }

    public void Apply(AEvent _)
    {
        ACount++;
    }

    public void Apply(BEvent _)
    {
        BCount++;
    }

    public void Apply(CEvent _)
    {
        CCount++;
    }

    public void Apply(DEvent _)
    {
        DCount++;
    }

    public void Apply(EEvent _)
    {
        ECount++;
    }

}

