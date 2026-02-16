using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten;
using Marten.Events.CodeGeneration;
using Marten.Events.Operations;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests;

public class appending_events_workflow_specs: IntegrationContext
{
    private const string TenantId = "KC";

    public appending_events_workflow_specs(DefaultStoreFixture fixture): base(fixture)
    {
    }

    private DocumentStore ConfigureStore(TestCase @case)
    {
        StoreOptions(opts =>
        {
            opts.EventGraph.StreamIdentity = @case.StreamIdentity;
            opts.EventGraph.TenancyStyle = @case.TenancyStyle;
            if (@case.AppendMode == EventAppendMode.Quick)
            {
                opts.Events.AppendMode = EventAppendMode.Quick;
            }
        });
        return theStore;
    }

    private IDocumentSession OpenSession(DocumentStore store, TenancyStyle tenancyStyle)
    {
        return tenancyStyle == TenancyStyle.Conjoined
            ? store.LightweightSession(TenantId)
            : store.LightweightSession();
    }

    private async Task<StreamAction> StartNewStream(DocumentStore store, Guid streamId)
    {
        var events = new object[] { new AEvent(), new BEvent(), new CEvent(), new DEvent() };
        using var session = OpenSession(store, store.Events.TenancyStyle);

        session.Listeners.Add(new EventMetadataChecker());

        if (store.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            session.Events.StartStream(streamId, events);
            await session.SaveChangesAsync();

            var stream = StreamAction.Append(store.Events, streamId);
            stream.Version = 4;
            stream.TenantId = TenantId;

            return stream;
        }
        else
        {
            session.Events.StartStream(streamId.ToString(), events);
            await session.SaveChangesAsync();

            var stream = StreamAction.Start(store.Events, streamId.ToString(), new AEvent());
            stream.Version = 4;
            stream.TenantId = TenantId;

            return stream;
        }
    }

    private StreamAction CreateNewStream(DocumentStore store)
    {
        var events = new IEvent[] { new Event<AEvent>(new AEvent()) };
        var stream = store.Events.StreamIdentity == StreamIdentity.AsGuid
            ? StreamAction.Start(Guid.NewGuid(), events)
            : StreamAction.Start(Guid.NewGuid().ToString(), events);

        stream.TenantId = TenantId;
        stream.Version = 1;

        return stream;
    }

    private StreamAction ToEventStream(DocumentStore store, Guid streamId)
    {
        if (store.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            var stream = StreamAction.Start(store.Events, streamId, new AEvent());
            stream.TenantId = TenantId;
            return stream;
        }
        else
        {
            var stream = StreamAction.Start(store.Events, streamId.ToString(), new AEvent());
            stream.TenantId = TenantId;
            return stream;
        }
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task can_fetch_stream_async(TestCase @case)
    {
        var store = ConfigureStore(@case);
        var streamId = Guid.NewGuid();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await StartNewStream(store, streamId);
        await using var query = store.QuerySession();

        var builder = EventDocumentStorageGenerator.GenerateStorage(store.Options);
        var handler = builder.QueryForStream(ToEventStream(store, streamId));

        var state = await query.As<QuerySession>().ExecuteHandlerAsync(handler, CancellationToken.None);
        state.ShouldNotBeNull();
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task can_fetch_stream_sync(TestCase @case)
    {
        var store = ConfigureStore(@case);
        var streamId = Guid.NewGuid();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await StartNewStream(store, streamId);
        using var query = store.QuerySession();

        var builder = EventDocumentStorageGenerator.GenerateStorage(store.Options);
        var handler = builder.QueryForStream(ToEventStream(store, streamId));

        var state = query.As<QuerySession>().ExecuteHandler(handler);
        state.ShouldNotBeNull();
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task can_insert_a_new_stream(TestCase @case)
    {
        var store = ConfigureStore(@case);
        var streamId = Guid.NewGuid();
        // This is just forcing the store to start the event storage
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await StartNewStream(store, streamId);

        var stream = CreateNewStream(store);
        var builder = EventDocumentStorageGenerator.GenerateStorage(store.Options);
        var op = builder.InsertStream(stream);

        await using var session = store.LightweightSession();
        session.QueueOperation(op);

        await session.SaveChangesAsync();
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task can_update_the_version_of_an_existing_stream_happy_path(TestCase @case)
    {
        var store = ConfigureStore(@case);
        var streamId = Guid.NewGuid();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        var stream = await StartNewStream(store, streamId);

        stream.ExpectedVersionOnServer = 4;
        stream.Version = 10;

        var builder = EventDocumentStorageGenerator.GenerateStorage(store.Options);
        var op = builder.UpdateStreamVersion(stream);

        await using var session = store.LightweightSession();
        session.QueueOperation(op);

        await session.SaveChangesAsync();

        var handler = builder.QueryForStream(stream);
        var state = session.As<QuerySession>().ExecuteHandler(handler);

        state.Version.ShouldBe(10);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task can_update_the_version_of_an_existing_stream_sad_path(TestCase @case)
    {
        var store = ConfigureStore(@case);
        var streamId = Guid.NewGuid();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        var stream = await StartNewStream(store, streamId);

        stream.ExpectedVersionOnServer = 3; // it's actually 4, so this should fail
        stream.Version = 10;

        var builder = EventDocumentStorageGenerator.GenerateStorage(store.Options);
        var op = builder.UpdateStreamVersion(stream);

        await using var session = store.LightweightSession();
        session.QueueOperation(op);

        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(() => session.SaveChangesAsync());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task can_establish_the_tombstone_stream_from_scratch(TestCase @case)
    {
        var store = ConfigureStore(@case);
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.EnsureStorageExistsAsync(typeof(IEvent));

        var operation = new EstablishTombstoneStream(store.Events, StorageConstants.DefaultTenantId);
        await using var session = (DocumentSessionBase)store.LightweightSession();

        var batch = new UpdateBatch(new[] { operation });
        await session.ExecuteBatchAsync(batch, CancellationToken.None);

        if (store.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            (await session.Events.FetchStreamStateAsync(Tombstone.StreamId)).ShouldNotBeNull();
        }
        else
        {
            (await session.Events.FetchStreamStateAsync(Tombstone.StreamKey)).ShouldNotBeNull();
        }
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task can_re_run_the_tombstone_stream(TestCase @case)
    {
        var store = ConfigureStore(@case);
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.EnsureStorageExistsAsync(typeof(IEvent));

        var operation = new EstablishTombstoneStream(store.Events, StorageConstants.DefaultTenantId);
        await using var session = (DocumentSessionBase)store.LightweightSession();

        var batch = new UpdateBatch(new[] { operation });

        await session.ExecuteBatchAsync(batch, CancellationToken.None);
        await session.ExecuteBatchAsync(batch, CancellationToken.None);

        if (store.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            (await session.Events.FetchStreamStateAsync(Tombstone.StreamId)).ShouldNotBeNull();
        }
        else
        {
            (await session.Events.FetchStreamStateAsync(Tombstone.StreamKey)).ShouldNotBeNull();
        }
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task exercise_tombstone_workflow_async(TestCase @case)
    {
        var store = ConfigureStore(@case);
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        await using var session = store.LightweightSession();

        if (store.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            session.Events.Append(Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent());
        }
        else
        {
            session.Events.Append(Guid.NewGuid().ToString(), new AEvent(), new BEvent(), new CEvent());
        }

        session.QueueOperation(new FailingOperation());

        await Should.ThrowAsync<DivideByZeroException>(async () =>
        {
            await session.SaveChangesAsync();
        });

        await using var session2 = store.LightweightSession();

        if (store.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            (await session2.Events.FetchStreamStateAsync(Tombstone.StreamId)).ShouldNotBeNull();

            var events = await session2.Events.FetchStreamAsync(Tombstone.StreamId);
            events.Any().ShouldBeTrue();
            foreach (var @event in events) @event.Data.ShouldBeOfType<Tombstone>();
        }
        else
        {
            (await session2.Events.FetchStreamStateAsync(Tombstone.StreamKey)).ShouldNotBeNull();

            var events = await session2.Events.FetchStreamAsync(Tombstone.StreamKey);
            events.Any().ShouldBeTrue();
            foreach (var @event in events) @event.Data.ShouldBeOfType<Tombstone>();
        }
    }

    public static IEnumerable<object[]> Data()
    {
        foreach (var identity in new[] { StreamIdentity.AsGuid, StreamIdentity.AsString })
        foreach (var tenancy in new[] { TenancyStyle.Single, TenancyStyle.Conjoined })
        foreach (var mode in new[] { EventAppendMode.Rich, EventAppendMode.Quick })
            yield return new object[] { new TestCase(identity, tenancy, mode) };
    }

    public record TestCase(StreamIdentity StreamIdentity, TenancyStyle TenancyStyle, EventAppendMode AppendMode)
    {
        public override string ToString()
        {
            return $"{StreamIdentity}, {TenancyStyle}, {AppendMode}";
        }
    }

    public class EventMetadataChecker: DocumentSessionListenerBase
    {
        public override Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        {
            var events = commit.GetEvents();
            foreach (var @event in events)
            {
                @event.TenantId.ShouldNotBeNull();
                @event.Timestamp.ShouldNotBe(DateTimeOffset.MinValue);
            }

            return Task.CompletedTask;
        }
    }

    public class FailingOperation: IStorageOperation
    {
        public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
        {
            builder.Append("select 1");
        }

        public Type DocumentType => null;

        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            throw new DivideByZeroException("Boom!");
        }

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            exceptions.Add(new DivideByZeroException("Boom!"));
            return Task.CompletedTask;
        }

        public OperationRole Role()
        {
            return OperationRole.Other;
        }
    }
}
