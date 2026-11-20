using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Internals;

/// <summary>
///     #6007: an event that commits inside a sequence range the high water mark already skipped is found, recorded,
///     reported, and folded into the async single-stream projections that handle it.
/// </summary>
public class late_events_in_skipped_ranges: DaemonContext
{
    private readonly ITestOutputHelper _output;

    public late_events_in_skipped_ranges(ITestOutputHelper output): base(output)
    {
        _output = output;

        // Its own schema, so no daemon another test left running on "daemon" probes these ranges first
        _schemaName = "late_events_6007";
    }

    private string Schema => theStore.Events.DatabaseSchemaName;

    [Fact]
    public async Task an_event_committed_after_a_capped_skip_is_recorded_reported_and_folded_into_its_stream_exactly_once()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 250.Milliseconds();
            opts.Projections.SkipStaleGapsDespiteLiveTransactionsAfter = 1.Seconds();
            opts.Projections.LateEventProbeInterval = 100.Milliseconds();
            opts.Projections.Add(new LateTallyProjection(), ProjectionLifecycle.Async);
        });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var metrics = new LateEventMetrics(theStore.Events.EventMappingFor<LateTallyTicked>().EventTypeName);
        var logger = new CapturingLogger(_output);

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new LateTallyStarted(), new LateTallyTicked(1), new LateTallyTicked(2));
            await session.SaveChangesAsync();
        }

        var daemon = ((MartenDatabase)theStore.Tenancy.Default.Database).StartProjectionDaemon(theStore, logger);
        await daemon.StartAllAsync();
        try
        {
            await theStore.Storage.Database.WaitForNonStaleProjectionDataAsync(30.Seconds());

            // A slow append to the same stream: it draws sequence 4 and stays open
            var (conn, tx, lateSequence) = await startOutstandingAppend(streamId, new LateTallyTicked(3));
            try
            {
                lateSequence.ShouldBe(4);

                // Committed above it, so the mark is held under the open append until the cap runs out
                await using (var session = theStore.LightweightSession())
                {
                    session.Events.StartStream(Guid.NewGuid(), new LateTallyStarted(), new LateTallyTicked(10));
                    await session.SaveChangesAsync();
                }

                await waitFor(async () => await highWaterMark() >= 6, 20.Seconds(),
                    "the high water mark never skipped past the open append");

                await waitFor(async () => await shardProgress("LateTally:All") >= 6, 20.Seconds(),
                    "the projection shard never got past the skipped range");

                // The skip is recorded with the sequence number that was still missing
                (await scalarString(
                        $"select reason from {Schema}.{EventProgressionSkippingTable.Name} where tenant_id = '' and ending_sequence = 6"))
                    .ShouldBe(nameof(HighWaterSkipReason.LiveTransaction));
                (await scalarString(
                        $"select array_to_string(gaps, ',') from {Schema}.{EventProgressionSkippingTable.Name} where ending_sequence = 6"))
                    .ShouldBe("4,4");

                await tx.CommitAsync();
            }
            finally
            {
                await conn.DisposeAsync();
            }

            // Recorded in the ledger
            await waitFor(async () => await scalar($"select count(*) from {Schema}.{LateEventsTable.Name}") == 1,
                15.Seconds(), "the late event was never recorded");

            await using (var conn2 = await openConnection())
            {
                await using var reader = await conn2
                    .CreateCommand(
                        $"select seq_id, stream_id, event_type, skip_starting_sequence, skip_ending_sequence, skip_reason from {Schema}.{LateEventsTable.Name}")
                    .ExecuteReaderAsync();
                (await reader.ReadAsync()).ShouldBeTrue();
                reader.GetInt64(0).ShouldBe(lateSequence);
                reader.GetString(1).ShouldBe(streamId.ToString());
                reader.GetString(2).ShouldBe(theStore.Events.EventMappingFor<LateTallyTicked>().EventTypeName);
                reader.GetInt64(3).ShouldBe(3);
                reader.GetInt64(4).ShouldBe(6);
                reader.GetString(5).ShouldBe(nameof(HighWaterSkipReason.LiveTransaction));
            }

            // Folded into the stream's document, exactly once
            await waitFor(async () =>
            {
                await using var query = theStore.QuerySession();
                var tally = await query.LoadAsync<LateTally>(streamId);
                return tally?.Numbers.Count == 3;
            }, 15.Seconds(), "the late event never reached the single stream projection");

            await waitFor(
                async () => await scalar(
                    $"select count(*) from {Schema}.{LateEventsTable.Name} where repaired_projections @> '{{LateTally}}'") == 1,
                15.Seconds(), "the repair was never recorded as done");

            // A few more probes must not fold it in again
            await Task.Delay(500);
            await using (var query = theStore.QuerySession())
            {
                var tally = await query.LoadAsync<LateTally>(streamId);
                tally.ShouldNotBeNull();
                tally.Numbers.ShouldBe([1, 2, 3]);
            }

            // Reported once
            metrics.Recorded.ShouldBe([nameof(HighWaterSkipReason.LiveTransaction)]);
            logger.Messages.Count(x =>
                    x.Level == LogLevel.Warning && x.Message.Contains($"Event {lateSequence} ")
                                                && x.Message.Contains("committed inside the sequence range (3, 6]"))
                .ShouldBe(1);
            logger.Messages.ShouldNotContain(x => x.Level == LogLevel.Error && x.Message.Contains("bug"));
        }
        finally
        {
            await daemon.StopAllAsync();
            daemon.Dispose();
        }
    }

    [Fact]
    public async Task an_event_filling_a_range_skipped_by_proof_is_logged_as_a_detector_bug()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 250.Milliseconds();
        });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var metrics = new LateEventMetrics(theStore.Events.EventMappingFor<LateTallyTicked>().EventTypeName);
        var logger = new CapturingLogger(_output);

        var streamId = await skipADeadGapByProof(logger);

        // Nothing could have filled sequence 4, so this is the detector's mistake to report
        await insertCommittedEvent(4, streamId, new LateTallyTicked(3));

        var detector = new HighWaterDetector((MartenDatabase)theStore.Tenancy.Default.Database, theStore.Events,
            logger);
        await detector.LateEvents.ProbeAsync(CancellationToken.None);

        (await scalarString($"select skip_reason from {Schema}.{LateEventsTable.Name} where seq_id = 4"))
            .ShouldBe(nameof(HighWaterSkipReason.Proven));
        logger.Messages.Count(x => x.Level == LogLevel.Error && x.Message.Contains("Event 4 ")
                                                             && x.Message.Contains("bug in Marten's high water detection"))
            .ShouldBe(1);
        metrics.Recorded.ShouldBe([nameof(HighWaterSkipReason.Proven)]);

        // Found once, whoever probes again
        await detector.LateEvents.ProbeAsync(CancellationToken.None);
        metrics.Recorded.Count.ShouldBe(1);
    }

    [Fact]
    public async Task a_skipped_range_that_stays_empty_past_the_window_is_closed_and_not_checked_again()
    {
        StoreOptions(opts =>
        {
            opts.Projections.StaleSequenceThreshold = 250.Milliseconds();
            opts.Projections.LateEventDetectionWindow = 300.Milliseconds();
        });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var metrics = new LateEventMetrics(theStore.Events.EventMappingFor<LateTallyTicked>().EventTypeName);
        var logger = new CapturingLogger(_output);

        var streamId = await skipADeadGapByProof(logger);
        var detector = new HighWaterDetector((MartenDatabase)theStore.Tenancy.Default.Database, theStore.Events,
            logger);

        // Still inside the window: checked, nothing there, stays open
        await detector.LateEvents.ProbeAsync(CancellationToken.None);
        (await scalar($"select count(*) from {Schema}.{EventProgressionSkippingTable.Name} where closed_at is null"))
            .ShouldBe(1);

        await Task.Delay(500);
        await detector.LateEvents.ProbeAsync(CancellationToken.None);
        (await scalar($"select count(*) from {Schema}.{EventProgressionSkippingTable.Name} where closed_at is null"))
            .ShouldBe(0);

        await insertCommittedEvent(4, streamId, new LateTallyTicked(3));
        await detector.LateEvents.ProbeAsync(CancellationToken.None);

        (await scalar($"select count(*) from {Schema}.{LateEventsTable.Name}")).ShouldBe(0);
        metrics.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task an_event_committed_after_a_manual_advance_is_recorded()
    {
        StoreOptions(opts => { });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var metrics = new LateEventMetrics(theStore.Events.EventMappingFor<LateTallyTicked>().EventTypeName);
        var logger = new CapturingLogger(_output);

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new LateTallyStarted(), new LateTallyTicked(1), new LateTallyTicked(2));
            await session.SaveChangesAsync();
        }

        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 3)");

        var (conn, tx, lateSequence) = await startOutstandingAppend(streamId, new LateTallyTicked(3));
        try
        {
            await using (var session = theStore.LightweightSession())
            {
                session.Events.StartStream(Guid.NewGuid(), new LateTallyStarted());
                await session.SaveChangesAsync();
            }

            await theStore.Advanced.AdvanceHighWaterMarkToLatestAsync(CancellationToken.None);
            await tx.CommitAsync();
        }
        finally
        {
            await conn.DisposeAsync();
        }

        (await scalarString(
                $"select reason || ':' || array_to_string(gaps, ',') from {Schema}.{EventProgressionSkippingTable.Name} where ending_sequence = 5"))
            .ShouldBe($"{nameof(HighWaterSkipReason.Manual)}:4,4");

        var detector = new HighWaterDetector((MartenDatabase)theStore.Tenancy.Default.Database, theStore.Events,
            logger);
        await detector.LateEvents.ProbeAsync(CancellationToken.None);

        (await scalarString($"select skip_reason from {Schema}.{LateEventsTable.Name} where seq_id = {lateSequence}"))
            .ShouldBe(nameof(HighWaterSkipReason.Manual));
        metrics.Recorded.ShouldBe([nameof(HighWaterSkipReason.Manual)]);
    }

    [Fact]
    public async Task nothing_is_recorded_when_no_range_was_skipped()
    {
        StoreOptions(opts => { });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var metrics = new LateEventMetrics(theStore.Events.EventMappingFor<LateTallyTicked>().EventTypeName);

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(Guid.NewGuid(), new LateTallyStarted(), new LateTallyTicked(1));
            await session.SaveChangesAsync();
        }

        var detector = new HighWaterDetector((MartenDatabase)theStore.Tenancy.Default.Database, theStore.Events,
            new CapturingLogger(_output));
        (await detector.Detect(CancellationToken.None)).CurrentMark.ShouldBe(2);
        await detector.LateEvents.ProbeAsync(CancellationToken.None);

        (await scalar($"select count(*) from {Schema}.{LateEventsTable.Name}")).ShouldBe(0);
        metrics.Recorded.ShouldBeEmpty();
    }

    #region helpers

    // Stream with events 1..3, mark at 3, sequence 4 burned by a rolled-back append, 5..6 committed, and a
    // detector that skips (3, 6] because nothing alive could fill 4
    private async Task<Guid> skipADeadGapByProof(ILogger logger)
    {
        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new LateTallyStarted(), new LateTallyTicked(1), new LateTallyTicked(2));
            await session.SaveChangesAsync();
        }

        await execute($"select {Schema}.mt_mark_event_progression('HighWaterMark', 3)");

        var (conn, tx, seq) = await startOutstandingAppend(streamId, new LateTallyTicked(3));
        try
        {
            seq.ShouldBe(4);
            await using (var session = theStore.LightweightSession())
            {
                session.Events.StartStream(Guid.NewGuid(), new LateTallyStarted(), new LateTallyTicked(10));
                await session.SaveChangesAsync();
            }

            await tx.RollbackAsync();
        }
        finally
        {
            await conn.DisposeAsync();
        }

        var detector = new HighWaterDetector((MartenDatabase)theStore.Tenancy.Default.Database, theStore.Events,
            logger);
        (await detector.DetectInSafeZone(CancellationToken.None)).CurrentMark.ShouldBe(3);
        await Task.Delay(400);
        var skipped = await detector.DetectInSafeZone(CancellationToken.None);
        skipped.CurrentMark.ShouldBe(6);
        skipped.IncludesSkipping.ShouldBeTrue();

        (await scalarString(
                $"select reason || ':' || array_to_string(gaps, ',') from {Schema}.{EventProgressionSkippingTable.Name} where ending_sequence = 6"))
            .ShouldBe($"{nameof(HighWaterSkipReason.Proven)}:4,4");

        return streamId;
    }

    private async Task<NpgsqlConnection> openConnection()
    {
        var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task<long> scalar(string sql)
    {
        await using var conn = await openConnection();
        var raw = await conn.CreateCommand(sql).ExecuteScalarAsync();
        return raw is long l ? l : Convert.ToInt64(raw ?? 0L);
    }

    private async Task<string> scalarString(string sql)
    {
        await using var conn = await openConnection();
        return (string)(await conn.CreateCommand(sql).ExecuteScalarAsync())!;
    }

    private async Task execute(string sql)
    {
        await using var conn = await openConnection();
        await conn.CreateCommand(sql).ExecuteNonQueryAsync();
    }

    private Task<long> highWaterMark()
        => scalar($"select coalesce(max(last_seq_id), 0) from {Schema}.mt_event_progression where name = 'HighWaterMark'");

    private Task<long> shardProgress(string shard)
        => scalar($"select coalesce(max(last_seq_id), 0) from {Schema}.mt_event_progression where name = '{shard}'");

    private string insertEventSql(long seq, Guid streamId, object @event)
    {
        var mapping = theStore.Events.EventMappingFor(@event.GetType());
        var json = theStore.Options.Serializer().ToJson(@event).Replace("'", "''");

        return $@"
insert into {Schema}.mt_events(seq_id, id, stream_id, version, data, type, timestamp, tenant_id, mt_dotnet_type, is_archived)
select {seq}, gen_random_uuid(), '{streamId}', (select max(version) + 1 from {Schema}.mt_events where stream_id = '{streamId}'),
       '{json}'::jsonb, '{mapping.EventTypeName}', now(), '*DEFAULT*', '{mapping.DotNetTypeName}', false";
    }

    // A slow append to an existing stream: draws the next sequence number and writes its event without committing
    private async Task<(NpgsqlConnection conn, NpgsqlTransaction tx, long seq)> startOutstandingAppend(Guid streamId,
        object @event)
    {
        var conn = await openConnection();
        var tx = await conn.BeginTransactionAsync();
        var seq = (long)(await conn.CreateCommand($"select nextval('{Schema}.mt_events_sequence')")
            .ExecuteScalarAsync())!;
        await conn.CreateCommand(insertEventSql(seq, streamId, @event)).ExecuteNonQueryAsync();
        return (conn, tx, seq);
    }

    private Task insertCommittedEvent(long seq, Guid streamId, object @event) =>
        execute(insertEventSql(seq, streamId, @event));

    private static async Task waitFor(Func<Task<bool>> condition, TimeSpan timeout, string failure)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }

        throw new ShouldAssertException(failure);
    }

    // Listens to the marten.daemon.late_events counter for this test's event type only
    private sealed class LateEventMetrics: IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly object _gate = new();
        private readonly List<string> _recorded = new();
        private readonly string _eventType;

        public LateEventMetrics(string eventType)
        {
            _eventType = eventType;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Name == OpenTelemetryOptions.LateEventsMetricName) listener.EnableMeasurementEvents(instrument);
            };

            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                string? eventType = null;
                string? reason = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "event.type") eventType = tag.Value as string;
                    if (tag.Key == OpenTelemetryOptions.SkipReasonTag) reason = tag.Value as string;
                }

                if (eventType != _eventType) return;

                lock (_gate)
                {
                    for (var i = 0; i < value; i++) _recorded.Add(reason!);
                }
            });

            _listener.Start();
        }

        public IReadOnlyList<string> Recorded
        {
            get
            {
                lock (_gate)
                {
                    return _recorded.ToList();
                }
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class CapturingLogger: ILogger
    {
        private readonly ITestOutputHelper _output;
        private readonly object _gate = new();
        private readonly List<(LogLevel Level, string Message)> _messages = new();

        public CapturingLogger(ITestOutputHelper output) => _output = output;

        public IReadOnlyList<(LogLevel Level, string Message)> Messages
        {
            get
            {
                lock (_gate)
                {
                    return _messages.ToList();
                }
            }
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            lock (_gate)
            {
                _messages.Add((logLevel, message));
            }

            try
            {
                _output.WriteLine($"{logLevel}: {message}{(exception == null ? "" : " " + exception)}");
            }
            catch (InvalidOperationException)
            {
                // The test is over
            }
        }

        private sealed class NullScope: IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    #endregion
}

public record LateTallyStarted;

public record LateTallyTicked(int Number);

public class LateTally
{
    public Guid Id { get; set; }
    public List<int> Numbers { get; set; } = new();
}

public class LateTallyProjection: SingleStreamProjection<LateTally, Guid>
{
    public LateTallyProjection()
    {
        Name = "LateTally";
    }

    public LateTally Create(LateTallyStarted _) => new();

    public void Apply(LateTallyTicked e, LateTally tally) => tally.Numbers.Add(e.Number);
}
