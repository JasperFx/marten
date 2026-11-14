using System.Diagnostics;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;

namespace DcbLoadTest;

/// <summary>
/// 4. mt_dcb_tag_version growth and HOT-update ratio over a long run of decisions, with and without
/// DcbTagVersionFillFactor. A decision reads one customer's boundary (customer AND OrderPlaced) and appends
/// an OrderShipped tagged with that customer, across a large tag set chosen uniformly at random, so updates
/// land on every heap page rather than one hot page.
/// </summary>
public static class GrowthScenario
{
    public static async Task Run(Settings settings)
    {
        var tagValues = settings.Int("tag-values", 200_000);
        var seconds = settings.Int("seconds", 300);
        var writers = settings.Int("writers", 16);
        var sampleEvery = settings.Int("sample-seconds", 30);
        var fillFactors = settings.Get("fillfactors", "none,70").Split(',')
            .Select(x => x == "none" ? (int?)null : int.Parse(x)).ToArray();
        var mode = settings.Modes[0];

        Report.Line($"## 4. mt_dcb_tag_version growth over {seconds}s of decisions");
        Report.Line();
        Report.Header(settings);
        Report.Line($"{mode}; {tagValues:N0} customers pre-created (one OrderPlaced each), then {writers} writers for {seconds}s, " +
                    "each decision reading customer AND OrderPlaced and appending OrderShipped for a uniformly random customer. " +
                    "Autovacuum at PostgreSQL defaults. Sizes are the heap / indexes of mt_dcb_tag_version.");
        Report.Line();

        var summary = new List<string[]>();
        foreach (var scope in settings.Scopes)
        foreach (var fillFactor in fillFactors)
        {
            var label = $"{scope}, fillfactor {(fillFactor?.ToString() ?? "default (100)")}";
            var schema = $"dcb_grow_{Stores.Short(scope)}_{fillFactor?.ToString() ?? "def"}";
            var store = await Stores.Fresh(settings, schema, mode, scope, fillFactor: fillFactor);
            var customers = await Precreate(store, tagValues);

            await Pg.SettleStats(settings);
            var start = await Pg.Stats(settings, schema, "mt_dcb_tag_version");
            Report.Progress($"{label}: {start.LiveTuples:N0} version rows, heap {Report.Mb(start.HeapBytes)} MB");

            var samples = new List<string[]>
            {
                Sample(0, 0, start, start)
            };

            long saves = 0;
            long conflicts = 0;
            var latency = new Latencies();
            var stop = Stopwatch.StartNew();
            var deadline = TimeSpan.FromSeconds(seconds);

            var work = Enumerable.Range(0, writers).Select(w => Task.Run(async () =>
            {
                var random = new Random(w * 104729 + 17);
                while (stop.Elapsed < deadline)
                {
                    var customer = customers[random.Next(customers.Length)];
                    var started = Stopwatch.GetTimestamp();
                    try
                    {
                        await using var session = store.LightweightSession();
                        var boundary = await session.Events.FetchForWritingByTags<OrderAggregate>(
                            new EventTagQuery().Or(customer).AndEventsOfType<OrderPlaced>());
                        var shipped = session.Events.BuildEvent(new OrderShipped("TRK"));
                        shipped.WithTag(customer);
                        boundary.AppendOne(shipped);
                        await session.SaveChangesAsync();
                        Interlocked.Increment(ref saves);
                        latency.Add(Stopwatch.GetElapsedTime(started));
                    }
                    catch (Exception e) when (Errors.Is<DcbConcurrencyException>(e))
                    {
                        Interlocked.Increment(ref conflicts);
                    }
                }
            })).ToArray();

            var all = Task.WhenAll(work);
            var lastSaves = 0L;
            var lastTime = TimeSpan.Zero;
            while (!all.IsCompleted)
            {
                await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(sampleEvery)));
                if (all.IsCompleted) break;
                var now = stop.Elapsed;
                var current = Interlocked.Read(ref saves);
                var stats = await Pg.Stats(settings, schema, "mt_dcb_tag_version");
                samples.Add(Sample(now.TotalSeconds, (current - lastSaves) / (now - lastTime).TotalSeconds, stats, start));
                lastSaves = current;
                lastTime = now;
            }

            await all;
            var elapsed = stop.Elapsed;
            await Pg.SettleStats(settings);
            var end = await Pg.Stats(settings, schema, "mt_dcb_tag_version");
            samples.Add(Sample(elapsed.TotalSeconds, double.NaN, end, start));

            Report.Line($"### {label}");
            Report.Line();
            Report.Table(["t (s)", "saves/s in interval", "heap MB", "index MB", "dead tuples", "updates", "HOT %", "autovacuums"], samples);

            var delta = end.Minus(start);
            summary.Add([
                scope.ToString(), fillFactor?.ToString() ?? "default",
                Report.N0(saves), (saves / elapsed.TotalSeconds).ToString("N0"), Report.Ms(latency.Percentile(50)), Report.Ms(latency.Percentile(99)),
                conflicts.ToString(), Report.N0(start.LiveTuples), (delta.Updates / (double)Math.Max(saves, 1)).ToString("N2"),
                Report.Pct(delta.HotUpdates / (double)Math.Max(delta.Updates, 1)),
                $"{Report.Mb(start.HeapBytes)} -> {Report.Mb(end.HeapBytes)}", $"{Report.Mb(start.IndexBytes)} -> {Report.Mb(end.IndexBytes)}",
                delta.AutovacuumCount.ToString()
            ]);
            store.Dispose();
        }

        Report.Line("### Summary");
        Report.Line();
        Report.Table(
            ["Scope", "fillfactor", "saves", "saves/s", "p50 ms", "p99 ms", "conflicts", "version rows", "row updates/save",
                "HOT %", "heap MB", "index MB", "autovacuums"],
            summary);
    }

    private static string[] Sample(double seconds, double savesPerSec, TableStats stats, TableStats start)
    {
        var delta = stats.Minus(start);
        return
        [
            seconds.ToString("N0"), double.IsNaN(savesPerSec) || savesPerSec == 0 ? "-" : savesPerSec.ToString("N0"),
            Report.Mb(stats.HeapBytes), Report.Mb(stats.IndexBytes), Report.N0(stats.DeadTuples), Report.N0(delta.Updates),
            Report.Pct(delta.Updates == 0 ? double.NaN : delta.HotUpdates / (double)delta.Updates),
            delta.AutovacuumCount.ToString()
        ];
    }

    private static async Task<CustomerId[]> Precreate(IDocumentStore store, int count)
    {
        var customers = Enumerable.Range(0, count).Select(_ => new CustomerId(Guid.NewGuid())).ToArray();
        const int perSave = 500;
        var next = -perSave;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            while (true)
            {
                var first = Interlocked.Add(ref next, perSave);
                if (first >= count) return;
                await using var session = store.LightweightSession();
                for (var i = first; i < Math.Min(first + perSave, count); i++)
                {
                    var placed = session.Events.BuildEvent(new OrderPlaced($"ORD-{i}", 10m));
                    placed.WithTag(customers[i]);
                    session.Events.StartStream(Guid.NewGuid(), placed);
                }

                await session.SaveChangesAsync();
            }
        })));
        return customers;
    }
}
