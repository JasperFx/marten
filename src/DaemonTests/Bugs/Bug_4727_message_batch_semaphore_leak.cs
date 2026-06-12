using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.Aggregations;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.Internals;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// marten#4727 — composite projection rebuilds self-deadlock when a stage emits side-effect
/// messages. Several event slices run in parallel and each calls
/// <see cref="ProjectionUpdateBatch.CurrentMessageBatch"/> (via ProjectionBatch.PublishMessageAsync).
/// The double-checked lock there returned the freshly-created batch from INSIDE the critical
/// section but OUTSIDE the try/finally, so the second caller to win the semaphore returned
/// without releasing it — leaking the semaphore and deadlocking every remaining caller
/// (observed in production as an optimized composite rebuild frozen forever, all slices parked
/// on SemaphoreSlim.WaitAsync, idle CPU, no error).
///
/// This test forces that exact interleaving deterministically with a gated outbox: the first
/// caller holds the semaphore while it is blocked creating the batch; the others pile up on the
/// semaphore; then the gate is released. With the bug, the callers that already queued never
/// complete.
/// </summary>
public class Bug_4727_message_batch_semaphore_leak : OneOffConfigurationsContext
{
    [Fact]
    public async Task concurrent_CurrentMessageBatch_callers_must_not_deadlock()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outbox = new GatedMessageOutbox(gate);

        StoreOptions(opts => opts.Events.MessageOutbox = outbox);

        using var cts = new CancellationTokenSource(30.Seconds());
        var session = (DocumentSessionBase)theStore.LightweightSession();
        await using var batch = new ProjectionUpdateBatch(
            theStore.Options.Projections, session, ShardExecutionMode.Continuous, cts.Token);

        // Several concurrent callers, mirroring the parallel event slices of one composite stage.
        var callers = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => batch.CurrentMessageBatch(session).AsTask(), cts.Token))
            .ToArray();

        // Let every caller pass the first null-check and pile up on the semaphore (the first one
        // is blocked inside CreateBatch on the gate, holding the semaphore)...
        await Task.Delay(1.Seconds(), cts.Token);
        // ...then release the first CreateBatch so it sets the batch and exits the critical section.
        gate.SetResult();

        var all = Task.WhenAll(callers);
        var winner = await Task.WhenAny(all, Task.Delay(15.Seconds(), CancellationToken.None));

        winner.ShouldBe(all,
            "every concurrent CurrentMessageBatch caller must complete — a leaked semaphore deadlocks the queued callers");

        await all; // surface any exceptions
        callers.Select(t => t.Result).Distinct().Count()
            .ShouldBe(1, "all callers must observe the single shared message batch");
    }
}

/// <summary>
/// First <see cref="CreateBatch"/> call blocks on the gate (holding the ProjectionUpdateBatch
/// semaphore) so the other concurrent callers are guaranteed to queue on that semaphore before
/// the batch is published — the precise window that triggers the #4727 leak.
/// </summary>
internal class GatedMessageOutbox : IMessageOutbox
{
    private readonly TaskCompletionSource _gate;
    private int _count;

    public GatedMessageOutbox(TaskCompletionSource gate) => _gate = gate;

    public async ValueTask<IMessageBatch> CreateBatch(DocumentSessionBase session)
    {
        if (Interlocked.Increment(ref _count) == 1)
        {
            await _gate.Task.ConfigureAwait(false);
        }

        return new RecordingMessageBatch();
    }
}
