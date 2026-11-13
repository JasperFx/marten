using System.Diagnostics;
using JasperFx.Events;
using Marten;
using Marten.Events;

namespace DcbLoadTest;

/// <summary>
/// 1. Plain tagged-append throughput: no boundary read, so the only DCB cost is the producer-side bump
/// of mt_dcb_tag_version for every distinct tag (and, under the per-event-type scopes, every distinct
/// (tag, event type)) the save carries.
/// </summary>
public static class AppendScenario
{
    private sealed record Variant(string Name, string Description, int Saves, int Writers, int EventsPerSave,
        bool Tagged, int Regions);

    private sealed class Sample
    {
        public readonly List<double> EventsPerSec = new();
        public readonly List<double> SavesPerSec = new();
        public readonly Latencies Latency = new();
        public readonly List<double> VersionWritesPerSave = new();
    }

    public static async Task Run(Settings settings)
    {
        var repeats = settings.Int("repeats", 3);
        var variants = new[]
        {
            new Variant("single", "1 event, 1 tag (new customer) per save; sequential",
                settings.Int("single-saves", 2000), 1, 1, true, 0),
            new Variant("batch", "10 events of 2 types, tags customer (new) + region (1 of 1000); sequential",
                settings.Int("batch-saves", 400), 1, 10, true, 1000),
            new Variant("untagged", "as batch, no tags (baseline)",
                settings.Int("batch-saves", 400), 1, 10, false, 0),
            new Variant("concurrent-hot", $"as batch, {settings.Int("writers", 16)} writers, region is 1 of 10 (hot version rows)",
                settings.Int("concurrent-saves", 100), settings.Int("writers", 16), 10, true, 10),
        };

        var configs = (from mode in settings.Modes from scope in settings.Scopes select (mode, scope)).ToArray();
        var samples = new Dictionary<(string, DcbStorageMode, DcbConflictScope), Sample>();
        var versionRows = new Dictionary<(DcbStorageMode, DcbConflictScope), (long Rows, long Events, long Bytes)>();

        Report.Line("## 1. Tagged-append throughput");
        Report.Line();
        Report.Header(settings);

        for (var r = 0; r < repeats; r++)
        {
            foreach (var (mode, scope) in configs)
            {
                var schema = $"dcb_app_{mode.ToString().ToLowerInvariant()}_{Stores.Short(scope)}";
                var store = await Stores.Fresh(settings, schema, mode, scope);
                Report.Progress($"repeat {r + 1}/{repeats}: {mode} / {scope}");

                foreach (var variant in variants)
                {
                    var key = (variant.Name, mode, scope);
                    if (!samples.TryGetValue(key, out var sample)) samples[key] = sample = new Sample();

                    // Warm-up: code generation, connection pool, plans. Not measured.
                    await RunVariant(store, variant with { Saves = 20, Writers = 1 }, new Latencies());

                    await Pg.SettleStats(settings);
                    var before = await Pg.Stats(settings, schema, "mt_dcb_tag_version");

                    var latency = new Latencies();
                    var elapsed = await RunVariant(store, variant, latency);
                    var totalSaves = variant.Saves * variant.Writers;

                    await Pg.SettleStats(settings);
                    var after = await Pg.Stats(settings, schema, "mt_dcb_tag_version");
                    var delta = after.Minus(before);

                    sample.EventsPerSec.Add(totalSaves * variant.EventsPerSave / elapsed.TotalSeconds);
                    sample.SavesPerSec.Add(totalSaves / elapsed.TotalSeconds);
                    sample.Latency.AddRange(latency);
                    sample.VersionWritesPerSave.Add((double)(delta.Inserts + delta.Updates) / totalSaves);

                    Report.Progress(
                        $"  {variant.Name}: {totalSaves / elapsed.TotalSeconds:N0} saves/s, " +
                        $"{(double)(delta.Inserts + delta.Updates) / totalSaves:N1} version writes/save");
                }

                versionRows[(mode, scope)] = (
                    await Pg.Scalar(settings, $"select count(*) from {schema}.mt_dcb_tag_version"),
                    await Pg.Scalar(settings, $"select count(*) from {schema}.mt_events"),
                    await Pg.Scalar(settings, $"select pg_total_relation_size('{schema}.mt_dcb_tag_version')"));
                store.Dispose();
            }
        }

        Report.Line("### mt_dcb_tag_version after one repeat of every variant");
        Report.Line();
        Report.Table(["Storage", "Scope", "events", "version rows", "table + index MB"],
            configs.Select(c =>
            {
                var v = versionRows[c];
                return new[] { c.mode.ToString(), c.scope.ToString(), Report.N0(v.Events), Report.N0(v.Rows), Report.Mb(v.Bytes) };
            }));

        foreach (var variant in variants)
        {
            Report.Line($"### {variant.Name}: {variant.Description}");
            Report.Line();
            Report.Line($"{variant.Saves * variant.Writers} saves x {repeats} repeats; throughput is the median repeat (min-max), latency is per save, pooled across repeats.");
            Report.Line();

            var baseline = new Dictionary<DcbStorageMode, double>();
            Report.Table(
                ["Storage", "Scope", "events/s", "saves/s (min-max)", "p50 ms", "p99 ms", "version-row writes/save", "vs TagValue"],
                configs.Select(c =>
                {
                    var s = samples[(variant.Name, c.mode, c.scope)];
                    var median = Median(s.SavesPerSec);
                    if (c.scope == DcbConflictScope.TagValue) baseline[c.mode] = median;
                    var versus = c.scope != DcbConflictScope.TagValue && baseline.TryGetValue(c.mode, out var b)
                        ? $"{(median / b - 1) * 100:+0;-0}%"
                        : "";
                    return new[]
                    {
                        c.mode.ToString(), c.scope.ToString(), Report.N0(Median(s.EventsPerSec)),
                        $"{median:N0} ({s.SavesPerSec.Min():N0}-{s.SavesPerSec.Max():N0})",
                        Report.Ms(s.Latency.Percentile(50)), Report.Ms(s.Latency.Percentile(99)),
                        Median(s.VersionWritesPerSave).ToString("N1"), versus
                    };
                }));
        }
    }

    private static async Task<TimeSpan> RunVariant(IDocumentStore store, Variant variant, Latencies latency)
    {
        var sw = Stopwatch.StartNew();
        var writers = Enumerable.Range(0, variant.Writers).Select(w => Task.Run(async () =>
        {
            var random = new Random(w * 7919 + Environment.TickCount);
            for (var s = 0; s < variant.Saves; s++)
            {
                var started = Stopwatch.GetTimestamp();
                await using var session = store.LightweightSession();
                var streamId = Guid.NewGuid();
                var customer = new CustomerId(Guid.NewGuid());
                var region = variant.Regions > 0 ? new RegionId($"region-{random.Next(variant.Regions)}") : null;

                for (var e = 0; e < variant.EventsPerSave; e++)
                {
                    object data = e % 2 == 0
                        ? new OrderPlaced($"ORD-{s}-{e}", 10m + e)
                        : new OrderShipped($"TRK-{s}-{e}");

                    if (!variant.Tagged)
                    {
                        session.Events.Append(streamId, data);
                        continue;
                    }

                    var wrapped = session.Events.BuildEvent(data);
                    if (region is null) wrapped.WithTag(customer);
                    else wrapped.WithTag(customer, region);
                    session.Events.Append(streamId, wrapped);
                }

                await session.SaveChangesAsync();
                latency.Add(Stopwatch.GetElapsedTime(started));
            }
        })).ToArray();

        await Task.WhenAll(writers);
        return sw.Elapsed;
    }

    internal static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return double.NaN;
        var sorted = values.OrderBy(x => x).ToArray();
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
