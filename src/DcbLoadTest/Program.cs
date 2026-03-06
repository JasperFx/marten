using System.Diagnostics;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
const int SEED_STREAMS = 1000;       // Pre-seed this many streams to simulate a large DB
const int SEED_EVENTS_PER_STREAM = 10;
const int BENCH_STREAMS = 200;       // Streams to append during measurement
const int BENCH_EVENTS_PER_STREAM = 10;

var connectionString = Environment.GetEnvironmentVariable("marten_testing_database")
    ?? "Host=localhost;Port=5433;Database=marten_testing;Username=postgres;Password=postgres";

Console.WriteLine("DCB Load Test — Append into large database");
Console.WriteLine($"  Connection: {connectionString}");
Console.WriteLine($"  Seed: {SEED_STREAMS} streams x {SEED_EVENTS_PER_STREAM} events = {SEED_STREAMS * SEED_EVENTS_PER_STREAM} events");
Console.WriteLine($"  Bench: {BENCH_STREAMS} streams x {BENCH_EVENTS_PER_STREAM} events = {BENCH_STREAMS * BENCH_EVENTS_PER_STREAM} events");
Console.WriteLine(new string('-', 90));

var results = new List<(string Scenario, int Iterations, TimeSpan Elapsed)>();

// ---------------------------------------------------------------------------
// Helper: build a configured store
// ---------------------------------------------------------------------------
IDocumentStore BuildStore(EventAppendMode mode)
{
    return DocumentStore.For(opts =>
    {
        opts.Connection(connectionString);
        opts.Events.AppendMode = mode;
        opts.Events.AddEventType<OrderPlaced>();
        opts.Events.AddEventType<OrderShipped>();
        opts.Events.RegisterTagType<CustomerId>("customer");
        opts.Events.RegisterTagType<RegionId>("region");
    });
}

// ---------------------------------------------------------------------------
// Helper: seed a large database with tagged events
// ---------------------------------------------------------------------------
async Task SeedDatabase(IDocumentStore store)
{
    Console.Write($"  Seeding {SEED_STREAMS * SEED_EVENTS_PER_STREAM} tagged events...");
    var sw = Stopwatch.StartNew();

    for (var s = 0; s < SEED_STREAMS; s++)
    {
        await using var session = store.LightweightSession();
        var streamId = Guid.NewGuid();
        var customerId = new CustomerId(Guid.NewGuid());
        var regionId = new RegionId($"region-{s % 10}");

        for (var e = 0; e < SEED_EVENTS_PER_STREAM; e++)
        {
            object data = e % 2 == 0
                ? new OrderPlaced($"ORD-{s}-{e}", 99.99m + e)
                : new OrderShipped($"TRACK-{s}-{e}");

            var wrapped = session.Events.BuildEvent(data);
            wrapped.WithTag(customerId, regionId);
            session.Events.Append(streamId, wrapped);
        }

        await session.SaveChangesAsync();
    }

    sw.Stop();
    Console.WriteLine($" done in {sw.Elapsed.TotalSeconds:N1}s");
}

// ---------------------------------------------------------------------------
// Helper: benchmark appending new streams with tags into the already-large DB
// ---------------------------------------------------------------------------
async Task BenchmarkAppend(string name, IDocumentStore store, bool withTags)
{
    Console.Write($"  {name}...");
    var sw = Stopwatch.StartNew();
    var totalEvents = 0;

    for (var s = 0; s < BENCH_STREAMS; s++)
    {
        await using var session = store.LightweightSession();
        var streamId = Guid.NewGuid();
        var customerId = new CustomerId(Guid.NewGuid());
        var regionId = new RegionId($"region-{s % 10}");

        for (var e = 0; e < BENCH_EVENTS_PER_STREAM; e++)
        {
            object data = e % 2 == 0
                ? new OrderPlaced($"ORD-bench-{s}-{e}", 99.99m + e)
                : new OrderShipped($"TRACK-bench-{s}-{e}");

            if (withTags)
            {
                var wrapped = session.Events.BuildEvent(data);
                wrapped.WithTag(customerId, regionId);
                session.Events.Append(streamId, wrapped);
            }
            else
            {
                session.Events.Append(streamId, data);
            }
        }

        await session.SaveChangesAsync();
        totalEvents += BENCH_EVENTS_PER_STREAM;
    }

    sw.Stop();
    results.Add((name, totalEvents, sw.Elapsed));
    Console.WriteLine($" {totalEvents} events in {sw.Elapsed.TotalMilliseconds:N1}ms ({totalEvents / sw.Elapsed.TotalSeconds:N1} events/sec)");
}

// ===========================================================================
// Run benchmarks
// ===========================================================================

// --- Quick mode benchmarks (the target for optimization) ---
Console.WriteLine("\n=== Quick Mode (into large DB) ===");
{
    await using var store = BuildStore(EventAppendMode.Quick);
    await store.Advanced.ResetAllData();
    await SeedDatabase(store);

    await BenchmarkAppend("Quick No Tags", store, withTags: false);
    await BenchmarkAppend("Quick With Tags", store, withTags: true);
}

// --- Rich mode benchmarks (for comparison) ---
Console.WriteLine("\n=== Rich Mode (into large DB) ===");
{
    await using var store = BuildStore(EventAppendMode.Rich);
    await store.Advanced.ResetAllData();
    await SeedDatabase(store);

    await BenchmarkAppend("Rich No Tags", store, withTags: false);
    await BenchmarkAppend("Rich With Tags", store, withTags: true);
}

// ---------------------------------------------------------------------------
// Print results
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine(new string('=', 90));
Console.WriteLine($"{"Scenario",-40} {"Events",10} {"Total (ms)",12} {"Events/sec",12}");
Console.WriteLine(new string('-', 90));
foreach (var (scenario, iterations, elapsed) in results)
{
    var opsPerSec = elapsed.TotalSeconds > 0
        ? (iterations / elapsed.TotalSeconds).ToString("N1")
        : "N/A";
    Console.WriteLine($"{scenario,-40} {iterations,10} {elapsed.TotalMilliseconds,12:N1} {opsPerSec,12}");
}
Console.WriteLine(new string('=', 90));

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------
public record OrderPlaced(string OrderId, decimal Amount);
public record OrderShipped(string TrackingNumber);
public record CustomerId(Guid Value);
public record RegionId(string Value);

public class OrderAggregate
{
    public Guid Id { get; set; }
    public List<string> OrderIds { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public int ShipmentCount { get; set; }

    public void Apply(OrderPlaced e)
    {
        OrderIds.Add(e.OrderId);
        TotalAmount += e.Amount;
    }

    public void Apply(OrderShipped e)
    {
        ShipmentCount++;
    }
}
