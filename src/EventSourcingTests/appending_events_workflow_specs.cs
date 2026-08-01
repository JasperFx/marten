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
using Marten.Events;
using Marten.EventStorage;
using Marten.Events.Operations;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using ICommandBuilder = Weasel.Postgresql.ICommandBuilder;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// One store profile per EventAppendMode x StreamIdentity x TenancyStyle
/// combination, each in its own schema. This replaces the old "v4events"
/// TestCase pattern here plus its whole-file Quick-mode fork in
/// QuickAppend/quick_appending_events_workflow_specs.cs.
/// </summary>
public class AppendingWorkflowFixture: MultiStoreFixture
{
    public static readonly IReadOnlyDictionary<string, (EventAppendMode Mode, StreamIdentity Identity, TenancyStyle Tenancy)>
        Cases = buildCases();

    private static IReadOnlyDictionary<string, (EventAppendMode, StreamIdentity, TenancyStyle)> buildCases()
    {
        var cases = new Dictionary<string, (EventAppendMode, StreamIdentity, TenancyStyle)>();
        foreach (var mode in new[]
                 {
                     EventAppendMode.Rich, EventAppendMode.Quick, EventAppendMode.QuickWithServerTimestamps
                 })
        {
            foreach (var identity in new[] { StreamIdentity.AsGuid, StreamIdentity.AsString })
            {
                foreach (var tenancy in new[] { TenancyStyle.Single, TenancyStyle.Conjoined })
                {
                    var modeKey = mode == EventAppendMode.QuickWithServerTimestamps ? "qwst" : mode.ToString().ToLowerInvariant();
                    var identityKey = identity == StreamIdentity.AsGuid ? "guid" : "string";
                    var tenancyKey = tenancy == TenancyStyle.Conjoined ? "conjoined" : "vanilla";

                    cases.Add($"{modeKey}_{identityKey}_{tenancyKey}", (mode, identity, tenancy));
                }
            }
        }

        return cases;
    }

    public AppendingWorkflowFixture(): base("esworkflow")
    {
        foreach (var pair in Cases)
        {
            var (mode, identity, tenancy) = pair.Value;
            Profile(pair.Key, opts =>
            {
                opts.Events.AppendMode = mode;
                opts.Events.StreamIdentity = identity;
                opts.Events.TenancyStyle = tenancy;
            });
        }
    }
}

public class appending_events_workflow_specs: IClassFixture<AppendingWorkflowFixture>
{
    private readonly AppendingWorkflowFixture _fixture;

    public appending_events_workflow_specs(AppendingWorkflowFixture fixture)
    {
        _fixture = fixture;
    }

    public static IEnumerable<object[]> Profiles()
    {
        return AppendingWorkflowFixture.Cases.Keys.Select(key => new object[] { key });
    }

    private const string TenantId = "KC";

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

    private static async Task<StreamAction> startNewStream(DocumentStore store, Guid streamId)
    {
        var events = new object[] { new AEvent(), new BEvent(), new CEvent(), new DEvent() };
        using var session = store.Events.TenancyStyle == TenancyStyle.Conjoined
            ? store.LightweightSession(TenantId)
            : store.LightweightSession();

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

    private static StreamAction createNewStream(DocumentStore store)
    {
        var events = new IEvent[] { new Event<AEvent>(new AEvent()) };
        var stream = store.Events.StreamIdentity == StreamIdentity.AsGuid
            ? StreamAction.Start(Guid.NewGuid(), events)
            : StreamAction.Start(Guid.NewGuid().ToString(), events);

        stream.TenantId = TenantId;
        stream.Version = 1;

        return stream;
    }

    private static StreamAction toEventStream(DocumentStore store, Guid streamId)
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
    [MemberData(nameof(Profiles))]
    public async Task can_fetch_stream_async(string profile)
    {
        var store = _fixture.StoreFor(profile);
        var streamId = Guid.NewGuid();

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await startNewStream(store, streamId);
        await using var query = store.QuerySession();

        var builder = new ClosedShapeEventDocumentStorage(store.Options);
        var handler = builder.QueryForStream(toEventStream(store, streamId));

        var state = await query.As<QuerySession>().ExecuteHandlerAsync(handler, CancellationToken.None);
        state.ShouldNotBeNull();
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task can_insert_a_new_stream(string profile)
    {
        var store = _fixture.StoreFor(profile);

        // This is just forcing the store to start the event storage
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await startNewStream(store, Guid.NewGuid());

        var stream = createNewStream(store);
        var builder = new ClosedShapeEventDocumentStorage(store.Options);
        var op = builder.InsertStream(stream);

        await using var session = store.LightweightSession();
        session.QueueOperation(op);

        await session.SaveChangesAsync();
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task can_update_the_version_of_an_existing_stream_happy_path(string profile)
    {
        var store = _fixture.StoreFor(profile);

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        var stream = await startNewStream(store, Guid.NewGuid());

        stream.ExpectedVersionOnServer = 4;
        stream.Version = 10;

        var builder = new ClosedShapeEventDocumentStorage(store.Options);
        var op = builder.UpdateStreamVersion(stream);

        await using var session = store.LightweightSession();
        session.QueueOperation(op);

        await session.SaveChangesAsync();

        var handler = builder.QueryForStream(stream);
        var state = await session.As<QuerySession>().ExecuteHandlerAsync(handler, CancellationToken.None);

        state.Version.ShouldBe(10);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task can_update_the_version_of_an_existing_stream_sad_path(string profile)
    {
        var store = _fixture.StoreFor(profile);

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        var stream = await startNewStream(store, Guid.NewGuid());

        stream.ExpectedVersionOnServer = 3; // it's actually 4, so this should fail
        stream.Version = 10;

        var builder = new ClosedShapeEventDocumentStorage(store.Options);
        var op = builder.UpdateStreamVersion(stream);

        await using var session = store.LightweightSession();
        session.QueueOperation(op);

        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(() => session.SaveChangesAsync());
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task can_establish_the_tombstone_stream_from_scratch(string profile)
    {
        var store = _fixture.StoreFor(profile);

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
    [MemberData(nameof(Profiles))]
    public async Task can_re_run_the_tombstone_stream(string profile)
    {
        var store = _fixture.StoreFor(profile);

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
    [MemberData(nameof(Profiles))]
    public async Task exercise_tombstone_workflow_async(string profile)
    {
        var store = _fixture.StoreFor(profile);

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
            foreach (var @event in events)
            {
                @event.Data.ShouldBeOfType<Tombstone>();
            }
        }
        else
        {
            (await session2.Events.FetchStreamStateAsync(Tombstone.StreamKey)).ShouldNotBeNull();

            var events = await session2.Events.FetchStreamAsync(Tombstone.StreamKey);
            events.Any().ShouldBeTrue();
            foreach (var @event in events)
            {
                @event.Data.ShouldBeOfType<Tombstone>();
            }
        }
    }

    public class FailingOperation: IStorageOperation
    {
        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            builder.Append("select 1");
        }

        public Type DocumentType => null;

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
