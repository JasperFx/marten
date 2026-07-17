using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DaemonTests.Aggregations;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

// #4963 / jasperfx#480 acceptance against real Postgres: the opt-in blue/green side-effect gate. When a
// NEW version of a projection opts into GateSideEffectsBehindPriorVersion and starts behind the highest
// PRIOR version's persisted progression mark N, the daemon replays [floor..N] in Rebuild mode (side
// effects suppressed) before starting Continuous from N — so RaiseSideEffects only fires for events the
// previous version never processed. Each event here starts its own single-event stream, so a fired side
// effect is observable per stream and the [<=N suppressed] / [(N..N+M] fires exactly once] boundary is
// asserted directly against a recording outbox. (Resume-after-interruption, version-1, already-past and
// failure paths are covered at the unit level in jasperfx BlueGreenSideEffectGateTests.)
[Collection("blue_green_gate")]
public class blue_green_side_effect_gate : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    // Unique schema per test instance so the two versioned stores in one test share events + progression,
    // while different test methods stay fully isolated (each xUnit method gets a fresh class instance).
    private readonly string _schema = "bluegreen_gate_" + Guid.NewGuid().ToString("N")[..8];

    public blue_green_side_effect_gate(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        // Fresh schema so the two versioned stores below share events + progression but start clean.
        using var store = SeparateStore(_ => { });
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DocumentStore SeparateStore(Action<StoreOptions> configure)
        => DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
            configure(opts);
        });

    private static async Task<List<Guid>> AppendSingleEventStreams(IDocumentStore store, int count)
    {
        var ids = new List<Guid>();
        await using var session = store.LightweightSession();
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            session.Events.StartStream<GateTrip>(id, new GateTripStarted(id));
        }

        await session.SaveChangesAsync();
        return ids;
    }

    private static List<Guid> FiredSideEffects(RecordingMessageOutbox outbox) =>
        outbox.Batches.SelectMany(b => b.Messages).Select(m => m.message).OfType<GateTripActivated>()
            .Select(x => x.Id).ToList();

    [Fact]
    public async Task fresh_deploy_suppresses_side_effects_up_to_the_prior_mark_then_fires_past_it()
    {
        const int N = 20;
        const int M = 8;

        // Blue: V2 with no opt-in. Run it to completion so its progression mark lands at N and its
        // side effects fire for the whole [1..N] history.
        var blueOutbox = new RecordingMessageOutbox();
        using (var blue = SeparateStore(opts =>
               {
                   opts.Projections.Add(new GateTripProjection { Version = 2 }, ProjectionLifecycle.Async);
                   opts.Events.MessageOutbox = blueOutbox;
                   opts.Logger(new TestOutputMartenLogger(_output));
               }))
        {
            await AppendSingleEventStreams(blue, N);

            var blueDaemon = await blue.BuildProjectionDaemonAsync();
            await blueDaemon.StartAllAsync();
            await blueDaemon.Tracker.WaitForShardState("GateTrip:V2:All", N, 60.Seconds());
            await blueDaemon.StopAllAsync();

            FiredSideEffects(blueOutbox).Count.ShouldBe(N, "blue V2 fires side effects across its whole catch-up");
        }

        // Green: V3 opts into the gate. Append M more events, then start. The gate must replay [0..N] in
        // Rebuild mode (no side effects) and only fire for the (N..N+M] tail.
        var greenOutbox = new RecordingMessageOutbox();
        using var green = SeparateStore(opts =>
        {
            opts.Projections.Add(new GateTripProjection { Version = 3, GateSideEffectsBehindPriorVersion = true },
                ProjectionLifecycle.Async);
            opts.Events.MessageOutbox = greenOutbox;
            opts.Logger(new TestOutputMartenLogger(_output));
        });

        var tailIds = await AppendSingleEventStreams(green, M);

        var greenDaemon = await green.BuildProjectionDaemonAsync();
        await greenDaemon.StartAllAsync();
        await greenDaemon.Tracker.WaitForShardState("GateTrip:V3:All", N + M, 60.Seconds());
        await greenDaemon.StopAllAsync();

        var fired = FiredSideEffects(greenOutbox);

        // Exactly the M tail events fire side effects — nothing from the [1..N] history the prior
        // version already processed.
        fired.Count.ShouldBe(M);
        fired.OrderBy(x => x).ShouldBe(tailIds.OrderBy(x => x));

        // V3's documents are nonetheless correct over the FULL history (the warm-up applied [1..N]).
        await using var query = green.QuerySession();
        (await query.Query<GateTrip>().CountAsync()).ShouldBe(N + M);
    }

    [Fact]
    public async Task without_the_opt_in_the_new_version_re_fires_side_effects_over_all_history()
    {
        const int N = 15;
        const int M = 5;

        var blueOutbox = new RecordingMessageOutbox();
        using (var blue = SeparateStore(opts =>
               {
                   opts.Projections.Add(new GateTripProjection { Version = 2 }, ProjectionLifecycle.Async);
                   opts.Events.MessageOutbox = blueOutbox;
               }))
        {
            await AppendSingleEventStreams(blue, N);
            var blueDaemon = await blue.BuildProjectionDaemonAsync();
            await blueDaemon.StartAllAsync();
            await blueDaemon.Tracker.WaitForShardState("GateTrip:V2:All", N, 60.Seconds());
            await blueDaemon.StopAllAsync();
        }

        // V3 WITHOUT the opt-in: today's behavior — one continuous catch-up over the whole history, so
        // side effects fire for all N+M events. This is exactly the re-emission the gate exists to avoid.
        var greenOutbox = new RecordingMessageOutbox();
        using var green = SeparateStore(opts =>
        {
            opts.Projections.Add(new GateTripProjection { Version = 3 }, ProjectionLifecycle.Async);
            opts.Events.MessageOutbox = greenOutbox;
        });

        await AppendSingleEventStreams(green, M);

        var greenDaemon = await green.BuildProjectionDaemonAsync();
        await greenDaemon.StartAllAsync();
        await greenDaemon.Tracker.WaitForShardState("GateTrip:V3:All", N + M, 60.Seconds());
        await greenDaemon.StopAllAsync();

        FiredSideEffects(greenOutbox).Count.ShouldBe(N + M);
    }
}

public record GateTripStarted(Guid Id);

public record GateTripActivated(Guid Id);

public class GateTrip
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

public partial class GateTripProjection : SingleStreamProjection<GateTrip, Guid>
{
    public void Apply(GateTrip trip, GateTripStarted _) => trip.Count++;

    public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<GateTrip> slice)
    {
        // One observable side effect per stream that saw a Started event this batch. During a rebuild
        // (and the blue/green warm-up, which runs in Rebuild mode) the daemon suppresses this.
        if (slice.Events().OfType<IEvent<GateTripStarted>>().Any())
        {
            slice.PublishMessage(new GateTripActivated(slice.Snapshot?.Id ?? slice.Events().First().StreamId));
        }

        return new ValueTask();
    }
}
