using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

// Regression guard for https://github.com/JasperFx/marten/issues/4441.
//
// The original bug fired against Wolverine's MartenToWolverineMessageBatch +
// MessageContext.FlushOutgoingMessagesAsync (when paired with
// AddAsyncDaemon(Solo) + a SingleStreamProjection without a RaiseSideEffects
// override). The Marten-side flow — daemon CatchUpAsync → buildBatchAsync →
// ProjectionUpdateBatch.SpinUpMessageBatchAsync → IMessageOutbox.CreateBatch →
// listener Before/AfterCommit — is shared by every IMessageOutbox
// implementation, so the tests below pin the Marten contract: a non-blocking
// IMessageBatch must catch up cleanly and the listener hooks must fire even
// when the projection raises no side effects.

public class Bug_4441_force_catch_up_with_outbox
{
    public class LetterCounts
    {
        public Guid Id { get; set; }
        public int ACount { get; set; }
    }

    public record AEvent;

    // Plain SingleStreamProjection — no RaiseSideEffects override (the exact
    // shape that triggered the original Wolverine hang).
    public class LetterCountsProjection: SingleStreamProjection<LetterCounts, Guid>
    {
        public override LetterCounts Evolve(LetterCounts? snapshot, Guid id, IEvent e)
        {
            snapshot ??= new LetterCounts { Id = id };
            if (e.Data is AEvent) snapshot.ACount++;
            return snapshot;
        }
    }

    [Fact(Timeout = 30000)]
    public async Task force_catch_up_returns_for_async_daemon_without_side_effects()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "bug4441_default";
                    m.Projections.Add<LetterCountsProjection>(ProjectionLifecycle.Async);
                }).AddAsyncDaemon(DaemonMode.Solo);
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        using (var session = store.LightweightSession())
        {
            session.Events.StartStream<LetterCounts>(new AEvent(), new AEvent(), new AEvent());
            await session.SaveChangesAsync();
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var exceptions = await host.ForceAllMartenDaemonActivityToCatchUpAsync(cts.Token);
        exceptions.ShouldBeEmpty();
    }

    [Fact(Timeout = 60000)]
    public async Task force_catch_up_invokes_message_batch_lifecycle_with_custom_outbox()
    {
        var outbox = new RecordingOutbox();

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "bug4441_outbox";
                    m.Events.MessageOutbox = outbox;
                    m.Projections.Add<LetterCountsProjection>(ProjectionLifecycle.Async);
                }).AddAsyncDaemon(DaemonMode.Solo);
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        using (var session = store.LightweightSession())
        {
            session.Events.StartStream<LetterCounts>(new AEvent(), new AEvent());
            await session.SaveChangesAsync();
        }

        // Wider CT than `force_catch_up_returns_for_async_daemon_without_side_effects`
        // because the custom-outbox lifecycle path adds extra hops (CreateBatch →
        // BeforeCommit → AfterCommit per shard) on top of the catch-up loop —
        // see #4462 for the timing context.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var exceptions = await host.ForceAllMartenDaemonActivityToCatchUpAsync(cts.Token);
        exceptions.ShouldBeEmpty();

        // Pins the Marten contract that downstream outbox implementations rely on:
        // SpinUpMessageBatchAsync runs in Continuous mode → CreateBatch fires →
        // BeforeCommit/AfterCommit on the resulting IMessageBatch fire during
        // catch-up even when the projection raises no side effects. If any
        // listener step is silently skipped, downstream outboxes like
        // Wolverine's MartenToWolverineMessageBatch lose their flush hook.
        outbox.CreateBatchCount.ShouldBeGreaterThan(0);
        outbox.LastBatch.ShouldNotBeNull();
        outbox.LastBatch!.AfterCommitCount.ShouldBeGreaterThan(0);
    }

    private sealed class RecordingOutbox: IMessageOutbox
    {
        public int CreateBatchCount;
        public RecordingBatch? LastBatch;

        public ValueTask<IMessageBatch> CreateBatch(DocumentSessionBase session)
        {
            Interlocked.Increment(ref CreateBatchCount);
            LastBatch = new RecordingBatch();
            return new ValueTask<IMessageBatch>(LastBatch);
        }
    }

    private sealed class RecordingBatch: IMessageBatch
    {
        public int BeforeCommitCount;
        public int AfterCommitCount;
        public int PublishCount;

        public ValueTask PublishAsync<T>(T message, string tenantId)
        {
            Interlocked.Increment(ref PublishCount);
            return ValueTask.CompletedTask;
        }

        public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        {
            Interlocked.Increment(ref AfterCommitCount);
            return Task.CompletedTask;
        }

        public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        {
            Interlocked.Increment(ref BeforeCommitCount);
            return Task.CompletedTask;
        }
    }
}
