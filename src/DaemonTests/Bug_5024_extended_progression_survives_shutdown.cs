using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Shouldly;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

public record Bug5024Event();

public class Bug5024Stream { public Guid Id { get; set; } }

public partial class Bug5024Projection: SingleStreamProjection<Bug5024Stream, Guid>
{
    public void Apply(Bug5024Event @event, Bug5024Stream projection) { }
}

// #5024 Phase 3 — the Marten-side proof that the jasperfx#557 shutdown fix landed.
//
// The extended_progression_batch_write flake (#5022) had two shutdown-ordering symptoms, both upstream in
// JasperFx.Events (jasperfx#557), both consumed here via the 2.34.0 bump:
//
//   Symptom 1 (the flake): the ExtendedProgressionWriter's drain was fire-and-forgotten on shutdown, so a
//     terminal "Stopped" heartbeat queued during shutdown could land AFTER a later, deliberate write to
//     mt_event_progression and clobber the row. jasperfx#557 makes the async stop path await the writer's
//     drain, so once StopAllAsync returns every queued heartbeat has already been flushed and a subsequent
//     deliberate write is the last writer.
//
//   Symptom 2: the shared BatchWriteThrottle SemaphoreSlim was disposed while an in-flight batch commit was
//     still mid-finally, so its Release() threw ObjectDisposedException (seen logged on
//     Bug_2074_recovering_from_errors). jasperfx#557 orders the throttle disposal after in-flight releases
//     and guards the release sites.
//
// #5023 deliberately stopped exercising the live-then-stopped daemon to de-flake #5008; this test is the
// live-daemon variant that is now deterministic *because* the upstream drain is awaited. It belongs in
// Marten because the flake was reported against a Marten test surface (#5022).
public class Bug_5024_extended_progression_survives_shutdown: DaemonContext
{
    public Bug_5024_extended_progression_survives_shutdown(ITestOutputHelper output): base(output)
    {
    }

    private const string Shard = "Bug5024Stream:All";

    private async Task<object?> readColumnAsync(string column)
    {
        await using var session = theStore.QuerySession();
        var raw = await session.Connection
            .CreateCommand(
                $"select {column} from {theStore.Events.DatabaseSchemaName}.mt_event_progression where name = :name")
            .With("name", Shard)
            .ExecuteScalarAsync();

        return raw is null or DBNull ? null : raw;
    }

    [Fact]
    public async Task deliberate_write_after_shutdown_is_not_clobbered_by_a_late_stopped_heartbeat()
    {
        var logger = new CapturingLogger(_output);

        StoreOptions(x =>
        {
            x.Events.EnableExtendedProgressionTracking = true;
            x.Projections.Add(new Bug5024Projection(), ProjectionLifecycle.Async);
        });

        var database = theStore.Tenancy.Default.Database.As<MartenDatabase>();

        // Start a live daemon carrying the capturing logger so any shutdown-time ObjectDisposedException
        // (Symptom 2) is observable.
        var daemon = database.StartProjectionDaemon(theStore, logger);
        await daemon.StartAllAsync();

        try
        {
            // Seed and let the shard catch up so its mt_event_progression row exists (the extended-progression
            // write takes its UPDATE path against a real row).
            await using (var session = theStore.LightweightSession())
            {
                for (var i = 0; i < 10; i++)
                {
                    session.Events.Append(Guid.NewGuid(), new Bug5024Event());
                }

                await session.SaveChangesAsync();
            }

            await daemon.Tracker.WaitForShardState(new ShardState(Shard, 10), 30.Seconds());

            // The real shutdown path. Pre-jasperfx#557 the writer's drain was fire-and-forgotten here, so a
            // terminal "Stopped" heartbeat could still be in flight after this returns. With the drain awaited,
            // StopAllAsync returns only once every queued heartbeat has been flushed to the row.
            await daemon.StopAllAsync();

            // The deliberate, post-shutdown write. Because the drain is awaited above, this is unambiguously the
            // last writer — no late "Stopped" heartbeat can land after it.
            var deliberate = new ShardState(Shard, 10)
            {
                Action = ShardAction.Started,
                AgentStatus = "Deliberate",
                LastHeartbeat = DateTimeOffset.UtcNow
            };
            await database.WriteExtendedProgressionAsync(deliberate);

            // Symptom 1: the deliberate value survives — a fire-and-forgotten "Stopped" heartbeat would have
            // overwritten agent_status back to the terminal state here.
            (await readColumnAsync("agent_status")).ShouldBe("Deliberate");

            // Symptom 2: the batch-write throttle must not be disposed out from under an in-flight commit, so no
            // ObjectDisposedException should reach the log across the daemon's lifetime.
            logger.Exceptions.ShouldNotContain(e => e is ObjectDisposedException);
        }
        finally
        {
            daemon.Dispose();
        }
    }

    // Minimal capturing ILogger — TestLogger<T> only echoes to output, so it can't be asserted against.
    // Records every logged exception (thread-safe) and still forwards to the test output for debugging.
    private sealed class CapturingLogger: ILogger
    {
        private readonly ITestOutputHelper _output;
        private readonly object _gate = new();
        public List<Exception> Exceptions { get; } = new();

        public CapturingLogger(ITestOutputHelper output) => _output = output;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception != null)
            {
                lock (_gate)
                {
                    Exceptions.Add(exception);
                }

                _output.WriteLine($"{logLevel}: {formatter(state, exception)}");
                _output.WriteLine(exception.ToString());
            }
            else
            {
                _output.WriteLine($"{logLevel}: {formatter(state, exception)}");
            }
        }

        private sealed class NullScope: IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
