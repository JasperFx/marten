using System.Diagnostics;
using System.Threading.Channels;
using JasperFx;
using Marten.ScaleTesting.Domain;
using Spectre.Console;

namespace Marten.ScaleTesting.Seeding;

/// <summary>
/// Coordinates the full seed: reference data per tenant + interleaved event
/// batches → bounded <see cref="Channel{T}"/> → N parallel writer tasks each
/// owning a session-per-tenant + <c>SaveChangesAsync</c> per batch.
///
/// <para>
/// Idempotent. Before doing any work, queries <c>mt_streams</c> per tenant;
/// if the existing event count already meets the target the seeder exits
/// early.
/// </para>
/// </summary>
internal sealed class EventSeeder
{
    private readonly IDocumentStore _store;
    private readonly SeedOptions _options;

    public EventSeeder(IDocumentStore store, SeedOptions options)
    {
        _store = store;
        _options = options;
    }

    public async Task<SeedReport> RunAsync(CancellationToken token)
    {
        var sw = Stopwatch.StartNew();

        // Idempotency gate. If every tenant already has at least the target
        // event count, exit. We don't try to be cute about "partial" seeding —
        // either the tenant has enough events or it doesn't.
        var existing = await ExistingEventCountsAsync(token).ConfigureAwait(false);
        var tenantsNeedingWork = Enumerable.Range(0, _options.TenantCount)
            .Where(i => existing.GetValueOrDefault(_options.TenantId(i)) < _options.EventsPerTenant)
            .ToArray();

        if (tenantsNeedingWork.Length == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Seed skipped — all {_options.TenantCount} tenants already have ≥ {_options.EventsPerTenant} events.[/]");
            return new SeedReport(0, 0, sw.Elapsed, AlreadySeeded: true);
        }

        AnsiConsole.MarkupLine($"[blue]Seeding {tenantsNeedingWork.Length} tenant(s) × ~{_options.EventsPerTenant:N0} events each.[/]");
        AnsiConsole.MarkupLine($"[grey]Writers: {_options.WriterTasks} · batch buffer: {_options.BatchBufferCapacity} · seed: {_options.Seed}[/]");

        // Reference data per tenant. Synchronous-sequential so the writer
        // pool isn't pummeled with parallel schema-creation contention on
        // the first tenant.
        var refData = new Dictionary<string, TenantReferenceData>(tenantsNeedingWork.Length);
        foreach (var tenantIdx in tenantsNeedingWork)
        {
            var tenantId = _options.TenantId(tenantIdx);
            refData[tenantId] = await ReferenceDataSeeder.SeedAsync(_store, tenantId, tenantIdx, _options.Seed, token).ConfigureAwait(false);
        }

        var channel = Channel.CreateBounded<EventBatch>(new BoundedChannelOptions(_options.BatchBufferCapacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        long batchesWritten = 0;
        long eventsWritten = 0;

        // Consumers fan out so multiple sessions write in parallel — that's the
        // realistic shape for a production async-daemon-backed app.
        var consumers = new Task[_options.WriterTasks];
        for (var i = 0; i < _options.WriterTasks; i++)
        {
            consumers[i] = Task.Run(async () =>
            {
                await foreach (var batch in channel.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    await WriteBatchAsync(batch, token).ConfigureAwait(false);
                    Interlocked.Increment(ref batchesWritten);
                    Interlocked.Add(ref eventsWritten, batch.Events.Count);
                }
            }, token);
        }

        // Reporter ticker — every 5s, print throughput so a 15-min seed isn't
        // silent.
        using var reporterCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var reporter = Task.Run(async () =>
        {
            var lastEvents = 0L;
            var ticker = TimeSpan.FromSeconds(5);
            while (!reporterCts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(ticker, reporterCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                var snap = Interlocked.Read(ref eventsWritten);
                var delta = snap - lastEvents;
                lastEvents = snap;
                AnsiConsole.MarkupLine($"[grey]  ... {snap:N0} events written ({(delta / ticker.TotalSeconds):N0}/sec)[/]");
            }
        }, reporterCts.Token);

        // Producer is single-threaded but processes tenants round-robin —
        // keeps the channel fed from multiple tenants concurrently which the
        // writers can then route to per-tenant sessions.
        await ProduceAsync(tenantsNeedingWork, refData, channel.Writer, token).ConfigureAwait(false);
        channel.Writer.Complete();

        await Task.WhenAll(consumers).ConfigureAwait(false);
        await reporterCts.CancelAsync().ConfigureAwait(false);
        try { await reporter.ConfigureAwait(false); } catch (OperationCanceledException) { }

        sw.Stop();
        return new SeedReport(batchesWritten, eventsWritten, sw.Elapsed, AlreadySeeded: false);
    }

    private async Task ProduceAsync(int[] tenantsToSeed, IReadOnlyDictionary<string, TenantReferenceData> refData, ChannelWriter<EventBatch> writer, CancellationToken token)
    {
        // Generate the per-tenant interleavers upfront — cheap, in-memory.
        // The actual draining is the heavy bit and pushes through the channel.
        var interleavers = new List<(EventInterleaver Interleaver, IEnumerator<EventBatch> Cursor)>(tenantsToSeed.Length);
        var clock = DateTimeOffset.UtcNow.Date.AddYears(-1); // start a year ago

        foreach (var tenantIdx in tenantsToSeed)
        {
            var tenantId = _options.TenantId(tenantIdx);
            var interleaver = BuildInterleaver(tenantId, tenantIdx, refData[tenantId], clock);
            interleavers.Add((interleaver, interleaver.Drain().GetEnumerator()));
        }

        // Round-robin across tenants — keeps the channel "mixed" rather than
        // tenant-by-tenant, which more realistically exercises the daemon's
        // tenant-keyed slice fan-out.
        var alive = interleavers.Count;
        var i = 0;
        while (alive > 0 && !token.IsCancellationRequested)
        {
            var idx = i++ % interleavers.Count;
            var (_, cursor) = interleavers[idx];
            if (cursor is null) continue;
            if (cursor.MoveNext())
            {
                await writer.WriteAsync(cursor.Current, token).ConfigureAwait(false);
            }
            else
            {
                cursor.Dispose();
                interleavers[idx] = (interleavers[idx].Interleaver, null!);
                alive--;
            }
        }
    }

    private EventInterleaver BuildInterleaver(string tenantId, int tenantIdx, TenantReferenceData refData, DateTimeOffset clock)
    {
        var rng = new Random(HashCode.Combine(_options.Seed, tenantIdx, "tenant"));
        var interleaver = new EventInterleaver(tenantId, rng);

        // We size streams to roughly land at the per-tenant event target. The
        // realistic mix is appointment-dominated; weights inside the
        // interleaver bias the per-batch *order*, the per-stream counts here
        // bias the per-stream *volume*.
        //
        // Average event-count-per-stream heuristics from StreamGenerators:
        //   Appointment ≈ 6.5 evts/stream
        //   Board       ≈ 9 evts/stream
        //   ProviderShift ≈ 7 evts/stream
        var (apptStreams, boardStreams, shiftStreams) = sizeStreams(_options.EventsPerTenant);

        var boardIds = new Guid[boardStreams];
        for (var b = 0; b < boardStreams; b++)
        {
            boardIds[b] = Guid.NewGuid();
            var boardRng = new Random(HashCode.Combine(_options.Seed, tenantIdx, "board", b));
            interleaver.AddBoardStream(boardIds[b], StreamGenerators.Board(boardRng, DateOnly.FromDateTime(clock.UtcDateTime.AddDays(b % 30)), clock.AddDays(b % 30)));
        }

        for (var s = 0; s < shiftStreams; s++)
        {
            var shiftRng = new Random(HashCode.Combine(_options.Seed, tenantIdx, "shift", s));
            var boardId = boardIds[s % Math.Max(1, boardStreams)];
            var providerId = refData.Providers[s % refData.Providers.Length];
            interleaver.AddProviderShiftStream(Guid.NewGuid(), StreamGenerators.ProviderShift(shiftRng, boardId, providerId));
        }

        for (var a = 0; a < apptStreams; a++)
        {
            var apptRng = new Random(HashCode.Combine(_options.Seed, tenantIdx, "appt", a));
            var patient = refData.Patients[a % refData.Patients.Length];
            var board = boardIds[a % Math.Max(1, boardStreams)];
            var provider = refData.Providers[a % refData.Providers.Length];
            interleaver.AddAppointmentStream(Guid.NewGuid(), StreamGenerators.Appointment(apptRng, patient, board, provider, clock));
        }

        return interleaver;
    }

    /// <summary>
    /// Solves "how many streams of each kind do I need to roughly hit
    /// <paramref name="eventsTarget"/>?" using the StreamGenerators averages.
    /// 70 / 25 / 5 split for appointments / shifts / boards aligns with the
    /// interleaver's default per-batch weights.
    /// </summary>
    private static (int Appointments, int Boards, int Shifts) sizeStreams(int eventsTarget)
    {
        const double apptAvg = 6.5;
        const double boardAvg = 9.0;
        const double shiftAvg = 7.0;

        var apptEvents = eventsTarget * 0.70;
        var shiftEvents = eventsTarget * 0.25;
        var boardEvents = eventsTarget * 0.05;

        return (
            Math.Max(1, (int)Math.Round(apptEvents / apptAvg)),
            Math.Max(1, (int)Math.Round(boardEvents / boardAvg)),
            Math.Max(1, (int)Math.Round(shiftEvents / shiftAvg))
        );
    }

    private async Task WriteBatchAsync(EventBatch batch, CancellationToken token)
    {
        // One stream per batch (interleaver enforces this), so always StartStream.
        // StartStream<TAggregate> tags the stream with the aggregate type name
        // even though no projection registration is active in this Phase A
        // bootstrap — Phase B's rebuild reads the type tag back when picking
        // the right SingleStreamProjection.
        await using var session = _store.LightweightSession(batch.TenantId);
        startStream(session, batch);
        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    private static void startStream(IDocumentSession session, EventBatch batch)
    {
        if (batch.AggregateType == typeof(Appointment))
        {
            session.Events.StartStream<Appointment>(batch.StreamId, batch.Events);
        }
        else if (batch.AggregateType == typeof(Board))
        {
            session.Events.StartStream<Board>(batch.StreamId, batch.Events);
        }
        else if (batch.AggregateType == typeof(ProviderShift))
        {
            session.Events.StartStream<ProviderShift>(batch.StreamId, batch.Events);
        }
        else
        {
            throw new InvalidOperationException($"Unrecognised aggregate type {batch.AggregateType.Name} in seed batch.");
        }
    }

    private async Task<IReadOnlyDictionary<string, long>> ExistingEventCountsAsync(CancellationToken token)
    {
        // Cross-tenant rollup, so we can't use a per-tenant QuerySession (the
        // store has DefaultTenantUsageEnabled = false). Open a raw connection
        // off the configured database instead.
        var counts = new Dictionary<string, long>();
        var database = await _store.Storage.FindOrCreateDatabase(StorageConstants.DefaultTenantId).ConfigureAwait(false);
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"select tenant_id, count(*) from {_store.Options.Events.DatabaseSchemaName}.mt_events group by tenant_id";
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                counts[reader.GetString(0)] = reader.GetInt64(1);
            }
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
        {
            // mt_events doesn't exist yet — first-ever seed. Treat as "no
            // tenant has any events" so the caller proceeds.
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
        return counts;
    }
}

internal sealed record SeedReport(long Batches, long Events, TimeSpan Elapsed, bool AlreadySeeded);
