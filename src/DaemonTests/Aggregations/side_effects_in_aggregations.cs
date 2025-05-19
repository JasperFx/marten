using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.MultiTenancy;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Internal.Sessions;
using Marten.Metadata;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Aggregations;

public class side_effects_in_aggregations: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public side_effects_in_aggregations(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task add_events_single_stream_guid_identifier()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<Projection1>(ProjectionLifecycle.Async);
            opts.Logger(new TestOutputMartenLogger(_output));
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SideEffects1>(streamId, new AEvent(),
            new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(30.Seconds());

        var version1 = await theSession.LoadAsync<SideEffects1>(streamId);
        version1.A.ShouldBe(3);
        version1.B.ShouldBe(0);

        theSession.Events.Append(streamId, new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();


        // Prove the BEevent side effect happened as expected
        var state = await theSession.Events.FetchStreamStateAsync(streamId);
        var tries = 0;
        while (state.Version != 6 && tries < 10)
        {
            await Task.Delay(250.Milliseconds());
            state = await theSession.Events.FetchStreamStateAsync(streamId);
            tries++;
        }

        await daemon.WaitForNonStaleData(30.Seconds());

        var version2 = await theSession.LoadAsync<SideEffects1>(streamId);
        version2.A.ShouldBe(5);

        // This proves that the aggregate was updated the new new event
        version2.B.ShouldBe(1);

        // 5 events explicitly added, 1 from side effect
        version2.Version.ShouldBe(6);


        state.Version.ShouldBe(6);
    }

    [Fact]
    public async Task calls_side_effects_when_there_is_a_delete_event()
    {
        var outbox = new RecordingMessageOutbox();

        StoreOptions(opts =>
        {
            opts.Projections.Add<Projection1>(ProjectionLifecycle.Async);
            opts.Logger(new TestOutputMartenLogger(_output));
            opts.Events.MessageOutbox = outbox;
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SideEffects1>(streamId, new AEvent(),
            new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(30.Seconds());

        theSession.Events.Append(streamId, new EEvent());
        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(30.Seconds());

        // Should be deleted by now
        var version1 = await theSession.LoadAsync<SideEffects1>(streamId);
        version1.ShouldBeNull();

        outbox
            .Batches
            .SelectMany(x => x.Messages)
            .OfType<WasDeleted>().Single().Id.ShouldBe(streamId);

    }

    [Fact]
    public async Task add_events_single_stream_guid_identifier_when_starting_a_stream()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<Projection1>(ProjectionLifecycle.Async);
            opts.Logger(new TestOutputMartenLogger(_output));
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SideEffects1>(streamId, new AEvent(),
            new AEvent(), new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        // Prove the BEevent side effect happened as expected
        var state = await theSession.Events.FetchStreamStateAsync(streamId);
        var tries = 0;
        while (state.Version != 6 && tries < 10)
        {
            await Task.Delay(500.Milliseconds());
            state = await theSession.Events.FetchStreamStateAsync(streamId);
            tries++;
        }

        state.Version.ShouldBe(6);

        await daemon.WaitForNonStaleData(30.Seconds());

        var version2 = await theSession.LoadAsync<SideEffects1>(streamId);
        version2.A.ShouldBe(5);

        // This proves that the aggregate was updated the new new event
        version2.B.ShouldBe(1);

        // 5 events explicitly added, 1 from side effect
        version2.Version.ShouldBe(6);
        state.Version.ShouldBe(6);
    }

    [Fact]
    public async Task add_events_single_stream_string_identifier_when_starting_a_stream()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<Projection2>(ProjectionLifecycle.Async);
            opts.Logger(new TestOutputMartenLogger(_output));
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var streamKey = Guid.NewGuid().ToString();
        theSession.Events.StartStream<SideEffects2>(streamKey, new AEvent(),
            new AEvent(), new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        // Prove the BEevent side effect happened as expected
        var state = await theSession.Events.FetchStreamStateAsync(streamKey);
        var tries = 0;
        while (state.Version != 6 && tries < 10)
        {
            await Task.Delay(250.Milliseconds());
            state = await theSession.Events.FetchStreamStateAsync(streamKey);
            tries++;
        }

        state.Version.ShouldBe(6);

        await daemon.WaitForNonStaleData(30.Seconds());

        var version2 = await theSession.LoadAsync<SideEffects2>(streamKey);
        version2.A.ShouldBe(5);

        // This proves that the aggregate was updated the new new event
        version2.B.ShouldBe(1);

        // 5 events explicitly added, 1 from side effect
        version2.Version.ShouldBe(6);
        state.Version.ShouldBe(6);
    }

    [Fact]
    public async Task add_events_single_stream_string_identifier()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add<Projection2>(ProjectionLifecycle.Async);
            opts.Logger(new TestOutputMartenLogger(_output));
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var streamKey = Guid.NewGuid().ToString();
        theSession.Events.StartStream<SideEffects2>(streamKey, new AEvent(),
            new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(30.Seconds());

        var version1 = await theSession.LoadAsync<SideEffects2>(streamKey);
        version1.A.ShouldBe(3);
        version1.B.ShouldBe(0);

        theSession.Events.Append(streamKey, new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();


        // Prove the BEevent side effect happened as expected
        var state = await theSession.Events.FetchStreamStateAsync(streamKey);
        var tries = 0;
        while (state.Version != 6 && tries < 10)
        {
            await Task.Delay(250.Milliseconds());
            state = await theSession.Events.FetchStreamStateAsync(streamKey);
            tries++;
        }

        await daemon.WaitForNonStaleData(30.Seconds());

        var version2 = await theSession.LoadAsync<SideEffects2>(streamKey);
        version2.A.ShouldBe(5);

        // This proves that the aggregate was updated the new new event
        version2.B.ShouldBe(1);

        // 5 events explicitly added, 1 from side effect
        version2.Version.ShouldBe(6);

        state.Version.ShouldBe(6);
    }

    [Fact]
    public async Task side_effects_do_not_happen_in_rebuilds()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add<Projection2>(ProjectionLifecycle.Async);
            opts.Logger(new TestOutputMartenLogger(_output));

            opts.Events.AppendMode = EventAppendMode.Quick;
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var streamKey = Guid.NewGuid().ToString();
        theSession.Events.StartStream<SideEffects2>(streamKey, new AEvent(),
            new AEvent(), new AEvent(), new AEvent(), new AEvent());
        await theSession.SaveChangesAsync();

        // Prove the BEevent side effect happened as expected
        var state = await theSession.Events.FetchStreamStateAsync(streamKey);
        var tries = 0;
        while (state.Version != 6 && tries < 10)
        {
            await Task.Delay(250.Milliseconds());
            state = await theSession.Events.FetchStreamStateAsync(streamKey);
            tries++;
        }

        await daemon.WaitForNonStaleData(30.Seconds());

        var version1 = await theSession.LoadAsync<SideEffects2>(streamKey);
        version1.A.ShouldBe(5);
        version1.B.ShouldBe(1);

        await daemon.StopAllAsync();

        await daemon.RebuildProjectionAsync<Projection2>(5.Seconds(), CancellationToken.None);

        // No changes!
        var version2 = await theSession.LoadAsync<SideEffects2>(streamKey);
        version2.A.ShouldBe(5);
        version2.B.ShouldBe(1);
    }

    [Fact]
    public async Task publishing_messages_in_continuous_mode()
    {
        var outbox = new RecordingMessageOutbox();

        StoreOptions(opts =>
        {
            opts.Projections.Add<Projection3>(ProjectionLifecycle.Async);
            opts.Events.MessageOutbox = outbox;
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();
        var stream3 = Guid.NewGuid();

        theSession.Events.StartStream<SideEffects1>(stream1, new AEvent(), new AEvent());
        theSession.Events.StartStream<SideEffects1>(stream2, new AEvent(), new AEvent());
        theSession.Events.StartStream<SideEffects1>(stream3, new AEvent(), new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(120.Seconds());

        var expected = outbox.Batches.SelectMany(x => x.Messages).OfType<GotB>().Single();
        expected.streamId.ShouldBe(stream3);

        foreach (var batch in outbox.Batches)
        {
            batch.BeforeCommitWasCalled.ShouldBeTrue();
            batch.AfterCommitWasCalled.ShouldBeTrue();
        }
    }
}

public class Projection1: SingleStreamProjection<SideEffects1, Guid>
{
    public Projection1()
    {
        DeleteEvent<EEvent>();
    }

    public void Apply(SideEffects1 aggregate, AEvent _)
    {
        aggregate.A++;
    }

    public void Apply(SideEffects1 aggregate, BEvent _)
    {
        aggregate.B++;
    }

    public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<SideEffects1> slice)
    {
        if (slice.Snapshot?.A % 5 == 0)
        {
            slice.AppendEvent(new BEvent());
        }

        if (slice.Events().OfType<IEvent<EEvent>>().Any())
        {
            slice.PublishMessage(new WasDeleted(slice.Events().First().StreamId));
        }

        return new ValueTask();

    }
}

public class SideEffects1: IRevisioned
{
    public Guid Id { get; set; }
    public int A { get; set; }
    public int B { get; set; }
    public int C { get; set; }
    public int D { get; set; }
    public int Version { get; set; }
}

public record WasDeleted(Guid Id);

public class Projection2: SingleStreamProjection<SideEffects2, string>
{
    public void Apply(SideEffects2 aggregate, AEvent _)
    {
        aggregate.A++;
    }

    public void Apply(SideEffects2 aggregate, BEvent _)
    {
        aggregate.B++;
    }

    public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<SideEffects2> slice)
    {
        if (slice.Snapshot.A >= 5 && slice.Snapshot.B == 0)
        {
            slice.AppendEvent(new BEvent());
        }

        return new ValueTask();
    }
}

public class Projection3: SingleStreamProjection<SideEffects1, Guid>
{
    public void Apply(SideEffects1 aggregate, AEvent _)
    {
        aggregate.A++;
    }

    public void Apply(SideEffects1 aggregate, BEvent _)
    {

    }

    public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<SideEffects1> slice)
    {
        if (slice.Snapshot != null && slice.Events().OfType<IEvent<BEvent>>().Any())
        {
            slice.PublishMessage(new GotB(slice.Snapshot.Id));
        }

        return new ValueTask();
    }
}

public record GotB(Guid streamId);

public class SideEffects2: IRevisioned
{
    public string Id { get; set; }
    public int A { get; set; }
    public int B { get; set; }
    public int C { get; set; }
    public int D { get; set; }
    public int Version { get; set; }
}

public class RecordingMessageOutbox: IMessageOutbox
{
    public readonly List<RecordingMessageBatch> Batches = new();

    public ValueTask<IMessageBatch> CreateBatch(DocumentSessionBase session)
    {
        var batch = new RecordingMessageBatch();
        Batches.Add(batch);

        return new ValueTask<IMessageBatch>(batch);
    }
}

public record TenantMessage(string tenantId, object message);

public class RecordingMessageBatch: IMessageBatch
{
    public readonly List<TenantMessage> Messages = new();

    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        AfterCommitWasCalled = true;
        return Task.CompletedTask;
    }

    public bool AfterCommitWasCalled { get; set; }

    public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        BeforeCommitWasCalled = true;
        return Task.CompletedTask;
    }

    public bool BeforeCommitWasCalled { get; set; }
    public ValueTask PublishAsync<T>(T message, string tenantId)
    {
        Messages.Add(new TenantMessage(tenantId, message));
        return new ValueTask();
    }
}
