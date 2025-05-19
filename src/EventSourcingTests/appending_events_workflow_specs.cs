using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.CodeGeneration;
using Marten.Events.Operations;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Schema.Arguments;
using Marten.Services;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests;

[Collection("v4events")]
public class appending_events_workflow_specs
{
    private readonly ITestOutputHelper _output;

    public appending_events_workflow_specs(ITestOutputHelper output)
    {
        _output = output;
    }

    public class EventMetadataChecker : DocumentSessionListenerBase
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

    [Theory]
    [MemberData(nameof(Data))]
    public async Task can_fetch_stream_async(TestCase @case)
    {
        await @case.Store.Advanced.Clean.CompletelyRemoveAllAsync();
        await @case.StartNewStream(new TestOutputMartenLogger(_output));
        await using var query = @case.Store.QuerySession();

        var builder = EventDocumentStorageGenerator.GenerateStorage(@case.Store.Options);
        var handler = builder.QueryForStream(@case.ToEventStream());

        var state = await query.As<QuerySession>().ExecuteHandlerAsync(handler, CancellationToken.None);
        state.ShouldNotBeNull();
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task can_fetch_stream_sync(TestCase @case)
    {
        await @case.Store.Advanced.Clean.CompletelyRemoveAllAsync();
        await @case.StartNewStream();
        using var query = @case.Store.QuerySession();

        var builder = EventDocumentStorageGenerator.GenerateStorage(@case.Store.Options);
        var handler = builder.QueryForStream(@case.ToEventStream());

        var state = query.As<QuerySession>().ExecuteHandler(handler);
        state.ShouldNotBeNull();
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task can_insert_a_new_stream(TestCase @case)
    {
        // This is just forcing the store to start the event storage
        await @case.Store.Advanced.Clean.CompletelyRemoveAllAsync();
        await @case.StartNewStream();

        var stream = @case.CreateNewStream();
        var builder = EventDocumentStorageGenerator.GenerateStorage(@case.Store.Options);
        var op = builder.InsertStream(stream);

        await using var session = @case.Store.LightweightSession();
        session.QueueOperation(op);

        await session.SaveChangesAsync();
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task can_update_the_version_of_an_existing_stream_happy_path(TestCase @case)
    {
        await @case.Store.Advanced.Clean.CompletelyRemoveAllAsync();
        var stream = await @case.StartNewStream(new TestOutputMartenLogger(_output));

        stream.ExpectedVersionOnServer = 4;
        stream.Version = 10;

        var builder = EventDocumentStorageGenerator.GenerateStorage(@case.Store.Options);
        var op = builder.UpdateStreamVersion(stream);

        await using var session = @case.Store.LightweightSession();
        session.QueueOperation(op);

        session.Logger = new TestOutputMartenLogger(_output);
        await session.SaveChangesAsync();

        var handler = builder.QueryForStream(stream);
        var state = session.As<QuerySession>().ExecuteHandler(handler);

        state.Version.ShouldBe(10);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task can_update_the_version_of_an_existing_stream_sad_path(TestCase @case)
    {
        await @case.Store.Advanced.Clean.CompletelyRemoveAllAsync();
        var stream = await @case.StartNewStream();

        stream.ExpectedVersionOnServer = 3; // it's actually 4, so this should fail
        stream.Version = 10;

        var builder = EventDocumentStorageGenerator.GenerateStorage(@case.Store.Options);
        var op = builder.UpdateStreamVersion(stream);

        await using var session = @case.Store.LightweightSession();
        session.QueueOperation(op);

        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(() => session.SaveChangesAsync());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task can_establish_the_tombstone_stream_from_scratch(TestCase @case)
    {
        await @case.Store.Advanced.Clean.CompletelyRemoveAllAsync();
        await @case.Store.EnsureStorageExistsAsync(typeof(IEvent));

        var operation = new EstablishTombstoneStream(@case.Store.Events, StorageConstants.DefaultTenantId);
        await using var session = (DocumentSessionBase)@case.Store.LightweightSession();

        var batch = new UpdateBatch(new []{operation});
        await session.ExecuteBatchAsync(batch, CancellationToken.None);

        if (@case.Store.Events.StreamIdentity == StreamIdentity.AsGuid)
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
        await @case.Store.Advanced.Clean.CompletelyRemoveAllAsync();
        await @case.Store.EnsureStorageExistsAsync(typeof(IEvent));

        var operation = new EstablishTombstoneStream(@case.Store.Events, StorageConstants.DefaultTenantId);
        await using var session = (DocumentSessionBase)@case.Store.LightweightSession();

        var batch = new UpdateBatch(new []{operation});

        await session.ExecuteBatchAsync(batch, CancellationToken.None);
        await session.ExecuteBatchAsync(batch, CancellationToken.None);

        if (@case.Store.Events.StreamIdentity == StreamIdentity.AsGuid)
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
        await @case.Store.Advanced.Clean.CompletelyRemoveAllAsync();

        await using var session = @case.Store.LightweightSession();

        if (@case.Store.Events.StreamIdentity == StreamIdentity.AsGuid)
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

        await using var session2 = @case.Store.LightweightSession();

        if (@case.Store.Events.StreamIdentity == StreamIdentity.AsGuid)
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


    public static IEnumerable<object[]> Data()
    {
        return cases().Select(x => new object[] {x});
    }

    private static IEnumerable<TestCase> cases()
    {
        yield return new TestCase("Streams as Guid, Vanilla", e => e.StreamIdentity = StreamIdentity.AsGuid);
        yield return new TestCase("Streams as String, Vanilla", e => e.StreamIdentity = StreamIdentity.AsString);

        yield return new TestCase("Streams as Guid, Multi-tenanted", e =>
        {
            e.StreamIdentity = StreamIdentity.AsGuid;
            e.TenancyStyle = TenancyStyle.Conjoined;
        });

        yield return new TestCase("Streams as String, Multi-tenanted", e =>
        {
            e.StreamIdentity = StreamIdentity.AsString;
            e.TenancyStyle = TenancyStyle.Conjoined;
        });
    }

    public class TestCase : IDisposable
    {
        private readonly string _description;
        private readonly Lazy<DocumentStore> _store;

        public TestCase(string description, Action<EventGraph> config)
        {
            _description = description;

            _store = new Lazy<DocumentStore>(() =>
            {
                var store = DocumentStore.For(opts =>
                {
                    config(opts.EventGraph);
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "v4events";
                    opts.AutoCreateSchemaObjects = AutoCreate.All;
                });

                return store;
            });

            StreamId = Guid.NewGuid();
            TenantId = "KC";
        }

        internal DocumentStore Store => _store.Value;

        public async Task<StreamAction> StartNewStream(IMartenSessionLogger logger = null)
        {
            var events = new object[] {new AEvent(), new BEvent(), new CEvent(), new DEvent()};
            using var session = Store.Events.TenancyStyle == TenancyStyle.Conjoined
                ? Store.LightweightSession(TenantId)
                : Store.LightweightSession();

            session.Listeners.Add(new EventMetadataChecker());

            if (logger != null)
            {
                session.Logger = logger;
            }

            if (Store.Events.StreamIdentity == StreamIdentity.AsGuid)
            {
                session.Events.StartStream(StreamId, events);
                await session.SaveChangesAsync();

                var stream = StreamAction.Append(Store.Events, StreamId);
                stream.Version = 4;
                stream.TenantId = TenantId;

                return stream;
            }
            else
            {
                session.Events.StartStream(StreamId.ToString(), events);
                await session.SaveChangesAsync();

                var stream = StreamAction.Start(Store.Events, StreamId.ToString(), new AEvent());
                stream.Version = 4;
                stream.TenantId = TenantId;

                return stream;
            }
        }

        public StreamAction CreateNewStream()
        {
            var events = new IEvent[] {new Event<AEvent>(new AEvent())};
            var stream = Store.Events.StreamIdentity == StreamIdentity.AsGuid ? StreamAction.Start(Guid.NewGuid(), events) : StreamAction.Start(Guid.NewGuid().ToString(), events);

            stream.TenantId = TenantId;
            stream.Version = 1;

            return stream;
        }

        public string TenantId { get; set; }

        public Guid StreamId { get;  }

        public void Dispose()
        {
            if (_store.IsValueCreated)
            {
                _store.Value.Dispose();
            }
        }

        public override string ToString()
        {
            return _description;
        }

        public StreamAction ToEventStream()
        {
            if (Store.Events.StreamIdentity == StreamIdentity.AsGuid)
            {
                var stream = StreamAction.Start(Store.Events, StreamId, new AEvent());
                stream.TenantId = TenantId;

                return stream;
            }
            else
            {
                var stream = StreamAction.Start(Store.Events, StreamId.ToString(), new AEvent());
                stream.TenantId = TenantId;

                return stream;
            }
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
