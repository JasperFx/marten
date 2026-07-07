using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Marten.ScaleTesting.Instrumentation;

/// <summary>
/// #4684 Phase E.3: per-batch wall-clock breakdown, consumed entirely from the harness side.
///
/// The daemon (JasperFx.Events <c>SubscriptionMetrics</c>) already emits three spans per event
/// page on the store's <c>Marten</c> ActivitySource —
/// <c>marten.{projection}.{shard}.page.loading</c> (event fetch from <c>mt_events</c>),
/// <c>.page.grouping</c> (slicing / enrichment pre-processing) and
/// <c>.page.execution</c> (projection user code + operation building + the SQL flush of the
/// <c>ProjectionUpdateBatch</c>). Those spans only exist when something listens, so production
/// runs pay nothing; this listener IS the something when <c>--instrument</c> is on.
///
/// Npgsql ≥ 8 emits one Activity per command under the <c>Npgsql</c> source, parented to
/// whatever Activity is current — i.e. to the page span whose work issued the command. Walking
/// the parent chain from each command span attributes DB round-trips to the page span that
/// caused them, giving a per-batch round-trip count rather than the run-level total the
/// E.1 <see cref="NpgsqlCommandCounter"/> reports.
///
/// Correlation: the three page spans for one batch share a shard prefix and an
/// <c>event.floor</c> tag, so <c>(shard, floor)</c> stitches loading + grouping + execution
/// back into one <see cref="BatchRecord"/>. Sequence floors are strictly monotonic per shard,
/// so the key never collides within a run.
/// </summary>
internal sealed class BatchSpanSampler: IDisposable
{
    private const string PageMarker = ".page.";

    private readonly ActivityListener _listener;
    private readonly ConcurrentDictionary<string, StrongBox<long>> _roundTrips = new();
    private readonly ConcurrentDictionary<(string Shard, long Floor), BatchAccumulator> _batches = new();
    private bool _disposed;

    private BatchSpanSampler(string martenSourceName)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == martenSourceName || src.Name == "Npgsql",
            // AllData so the page spans carry their page.size / event.floor / event.ceiling tags.
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = OnStopped
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public static BatchSpanSampler Start(string martenSourceName = "Marten") => new(martenSourceName);

    private void OnStopped(Activity activity)
    {
        if (activity.Source.Name == "Npgsql")
        {
            AttributeCommandToPageSpan(activity);
            return;
        }

        var kindIndex = activity.OperationName.IndexOf(PageMarker, StringComparison.Ordinal);
        if (kindIndex < 0)
        {
            return;
        }

        var shard = activity.OperationName[..kindIndex];
        var kind = activity.OperationName[(kindIndex + PageMarker.Length)..];

        long floor = ReadLongTag(activity, "event.floor");
        var accumulator = _batches.GetOrAdd((shard, floor), _ => new BatchAccumulator());
        var elapsedMs = activity.Duration.TotalMilliseconds;
        var trips = _roundTrips.TryRemove(activity.SpanId.ToString(), out var box)
            ? Interlocked.Read(ref box.Value)
            : 0;

        switch (kind)
        {
            case "loading":
                accumulator.LoadingMs = elapsedMs;
                accumulator.LoadingRoundTrips = trips;
                break;
            case "grouping":
                accumulator.GroupingMs = elapsedMs;
                accumulator.GroupingRoundTrips = trips;
                break;
            case "execution":
                accumulator.ExecutionMs = elapsedMs;
                accumulator.ExecutionRoundTrips = trips;
                accumulator.Events = ReadLongTag(activity, "page.size");
                accumulator.Ceiling = ReadLongTag(activity, "event.ceiling");
                accumulator.CompletedAt = DateTimeOffset.UtcNow;
                break;
        }
    }

    private void AttributeCommandToPageSpan(Activity command)
    {
        // Connection lifecycle spans aren't round-trips issued by batch work
        if (command.OperationName is "Npgsql.OpenConnection" or "Npgsql.CloseConnection")
        {
            return;
        }

        for (var parent = command.Parent; parent != null; parent = parent.Parent)
        {
            if (parent.Source.Name != "Npgsql"
                && parent.OperationName.Contains(PageMarker, StringComparison.Ordinal))
            {
                var box = _roundTrips.GetOrAdd(parent.SpanId.ToString(), _ => new StrongBox<long>());
                Interlocked.Increment(ref box.Value);
                return;
            }
        }
    }

    private static long ReadLongTag(Activity activity, string tag)
    {
        var value = activity.GetTagItem(tag);
        return value switch
        {
            null => 0,
            long l => l,
            int i => i,
            string s when long.TryParse(s, out var parsed) => parsed,
            _ => 0
        };
    }

    /// <summary>
    /// Roll the raw span records up into finished batches. Only batches that saw an execution
    /// span count — a loading span with no matching execution at capture time is a batch still
    /// in flight when the run stopped.
    /// </summary>
    public IReadOnlyList<BatchRecord> Capture(string? tracePath = null)
    {
        var records = _batches
            .Where(kv => kv.Value.ExecutionMs.HasValue)
            .Select(kv => new BatchRecord(
                kv.Key.Shard,
                kv.Key.Floor,
                kv.Value.Ceiling,
                kv.Value.Events,
                kv.Value.LoadingMs ?? 0,
                kv.Value.GroupingMs ?? 0,
                kv.Value.ExecutionMs!.Value,
                (kv.Value.LoadingMs ?? 0) + (kv.Value.GroupingMs ?? 0) + kv.Value.ExecutionMs!.Value,
                kv.Value.LoadingRoundTrips + kv.Value.GroupingRoundTrips + kv.Value.ExecutionRoundTrips,
                kv.Value.CompletedAt))
            .OrderBy(r => r.CompletedAt)
            .ToArray();

        if (tracePath != null)
        {
            WriteTrace(tracePath, records);
        }

        return records;
    }

    private static void WriteTrace(string path, IReadOnlyList<BatchRecord> records)
    {
        using var writer = new StreamWriter(path, append: false);
        writer.WriteLine("completed_at,shard,floor,ceiling,events,loading_ms,grouping_ms,execution_ms,total_ms,round_trips");
        foreach (var r in records)
        {
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{r.CompletedAt:O},{r.Shard},{r.Floor},{r.Ceiling},{r.Events},{r.LoadingMs:F3},{r.GroupingMs:F3},{r.ExecutionMs:F3},{r.TotalMs:F3},{r.RoundTrips}"));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listener.Dispose();
    }

    private sealed class BatchAccumulator
    {
        public double? LoadingMs;
        public double? GroupingMs;
        public double? ExecutionMs;
        public long LoadingRoundTrips;
        public long GroupingRoundTrips;
        public long ExecutionRoundTrips;
        public long Events;
        public long Ceiling;
        public DateTimeOffset CompletedAt;
    }

    private sealed class StrongBox<T> where T : struct
    {
        public T Value;
    }
}

/// <summary>One completed event page (batch) as seen by <see cref="BatchSpanSampler"/>.</summary>
public sealed record BatchRecord(
    string Shard,
    long Floor,
    long Ceiling,
    long Events,
    double LoadingMs,
    double GroupingMs,
    double ExecutionMs,
    double TotalMs,
    long RoundTrips,
    DateTimeOffset CompletedAt);

/// <summary>
/// Percentile roll-up of the per-batch series, in the <c>perBatch.p50/p95/p99</c> shape the
/// #4684 issue specifies for <c>metrics.json</c>.
/// </summary>
public sealed record BatchBreakdownStats(
    int BatchCount,
    BatchPercentileRow P50,
    BatchPercentileRow P95,
    BatchPercentileRow P99,
    IReadOnlyDictionary<string, ShardBatchStats> PerShard)
{
    public static BatchBreakdownStats Empty { get; } = new(
        0, BatchPercentileRow.Zero, BatchPercentileRow.Zero, BatchPercentileRow.Zero,
        new Dictionary<string, ShardBatchStats>());

    public static BatchBreakdownStats From(IReadOnlyList<BatchRecord> records)
    {
        if (records.Count == 0)
        {
            return Empty;
        }

        var perShard = records
            .GroupBy(r => r.Shard)
            .ToDictionary(
                g => g.Key,
                g => new ShardBatchStats(
                    g.Count(),
                    g.Sum(x => x.Events),
                    Percentile(g.Select(x => x.TotalMs), 0.50),
                    g.Average(x => x.Events),
                    g.Average(x => x.RoundTrips)));

        return new BatchBreakdownStats(
            records.Count,
            RowAt(records, 0.50),
            RowAt(records, 0.95),
            RowAt(records, 0.99),
            perShard);
    }

    private static BatchPercentileRow RowAt(IReadOnlyList<BatchRecord> records, double q) =>
        new(
            Percentile(records.Select(r => r.TotalMs), q),
            Percentile(records.Select(r => r.LoadingMs), q),
            Percentile(records.Select(r => r.GroupingMs), q),
            Percentile(records.Select(r => r.ExecutionMs), q),
            Percentile(records.Select(r => (double)r.RoundTrips), q),
            Percentile(records.Select(r => (double)r.Events), q));

    private static double Percentile(IEnumerable<double> values, double q)
    {
        // Nearest-rank, matching ThroughputPercentiles — interpolation noise isn't worth it at
        // harness sample volumes.
        var sorted = values.OrderBy(x => x).ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var rank = (int)Math.Ceiling(q * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}

/// <summary>One percentile's view of the per-batch breakdown.</summary>
public sealed record BatchPercentileRow(
    double TotalMs,
    double EventFetchMs,
    double GroupingMs,
    double ExecutionMs,
    double RoundTripCount,
    double Events)
{
    public static BatchPercentileRow Zero { get; } = new(0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Per-shard batch summary. Under the composite executor each stage-crossing shard shows up
/// separately, so this is also the per-stage timing view (#4684 item 6) to the extent the
/// daemon exposes stages as shards.
/// </summary>
public sealed record ShardBatchStats(
    int Batches,
    long Events,
    double TotalMsP50,
    double MeanEventsPerBatch,
    double MeanRoundTripsPerBatch);
