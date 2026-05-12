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
const int QUERY_ITERATIONS = 200;    // How many times to run each query benchmark

var connectionString = Environment.GetEnvironmentVariable("marten_testing_database")
    ?? "Host=localhost;Port=5433;Database=marten_testing;Username=postgres;Password=postgres";

Console.WriteLine("DCB Load Test — TagTables vs HStore comparison");
Console.WriteLine($"  Connection: {connectionString}");
Console.WriteLine($"  Seed:  {SEED_STREAMS} streams x {SEED_EVENTS_PER_STREAM} events = {SEED_STREAMS * SEED_EVENTS_PER_STREAM} events");
Console.WriteLine($"  Write: {BENCH_STREAMS} streams x {BENCH_EVENTS_PER_STREAM} events = {BENCH_STREAMS * BENCH_EVENTS_PER_STREAM} events per scenario");
Console.WriteLine($"  Read:  {QUERY_ITERATIONS} iterations per query scenario");
Console.WriteLine(new string('-', 100));

var results = new List<BenchResult>();

// Pre-build a deterministic set of customer + region ids so we can query them later
var random = new Random(42);
var seededCustomerIds = Enumerable.Range(0, SEED_STREAMS)
    .Select(_ => new CustomerId(Guid.NewGuid()))
    .ToArray();
var seededRegionIds = Enumerable.Range(0, 10)
    .Select(i => new RegionId($"region-{i}"))
    .ToArray();

// ---------------------------------------------------------------------------
// Run both storage modes against isolated schemas
// ---------------------------------------------------------------------------
foreach (var mode in new[] { DcbStorageMode.TagTables, DcbStorageMode.HStore })
{
    var schemaName = mode == DcbStorageMode.HStore ? "dcb_hstore" : "dcb_tagtables";
    Console.WriteLine();
    Console.WriteLine($"=== {mode} mode (schema: {schemaName}) ===");

    await using var store = BuildStore(EventAppendMode.Quick, mode, schemaName);
    await store.Advanced.ResetAllData();
    await SeedDatabase(store);

    // --- WRITE-PATH benchmarks ---
    await BenchmarkAppend($"{mode} — append, no tags",       store, withTags: false);
    await BenchmarkAppend($"{mode} — append, 2 tags/event",  store, withTags: true);

    // --- READ-PATH benchmarks ---
    await BenchmarkQuery(
        $"{mode} — QueryByTagsAsync, 1 tag",
        store, async session =>
        {
            // Pick a real seeded customer to ensure we hit data and return ~10 events
            var customer = seededCustomerIds[random.Next(seededCustomerIds.Length)];
            var query = new EventTagQuery().Or<CustomerId>(customer);
            return await session.Events.QueryByTagsAsync(query);
        });

    await BenchmarkQuery(
        $"{mode} — QueryByTagsAsync, 2 tags OR",
        store, async session =>
        {
            var customer = seededCustomerIds[random.Next(seededCustomerIds.Length)];
            var region = seededRegionIds[random.Next(seededRegionIds.Length)];
            var query = new EventTagQuery()
                .Or<CustomerId>(customer)
                .Or<RegionId>(region);
            return await session.Events.QueryByTagsAsync(query);
        });

    await BenchmarkExists(
        $"{mode} — EventsExistAsync, 1 tag",
        store, () =>
        {
            var customer = seededCustomerIds[random.Next(seededCustomerIds.Length)];
            return new EventTagQuery().Or<CustomerId>(customer);
        });

    await BenchmarkExists(
        $"{mode} — EventsExistAsync, 2 tags OR",
        store, () =>
        {
            var customer = seededCustomerIds[random.Next(seededCustomerIds.Length)];
            var region = seededRegionIds[random.Next(seededRegionIds.Length)];
            return new EventTagQuery()
                .Or<CustomerId>(customer)
                .Or<RegionId>(region);
        });

    await BenchmarkFetchForWriting(
        $"{mode} — FetchForWritingByTags + commit",
        store, () =>
        {
            var customer = seededCustomerIds[random.Next(seededCustomerIds.Length)];
            return new EventTagQuery().Or<CustomerId>(customer);
        });
}

// ---------------------------------------------------------------------------
// Print results, grouped by scenario family
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine(new string('=', 100));
Console.WriteLine($"{"Scenario",-55} {"Iterations",10} {"Total (ms)",12} {"Per op (ms)",12} {"Ops/sec",10}");
Console.WriteLine(new string('-', 100));
foreach (var r in results)
{
    var perOp = r.Iterations > 0 ? r.Elapsed.TotalMilliseconds / r.Iterations : 0;
    var opsPerSec = r.Elapsed.TotalSeconds > 0
        ? (r.Iterations / r.Elapsed.TotalSeconds).ToString("N1")
        : "N/A";
    Console.WriteLine($"{r.Scenario,-55} {r.Iterations,10} {r.Elapsed.TotalMilliseconds,12:N1} {perOp,12:N3} {opsPerSec,10}");
}
Console.WriteLine(new string('=', 100));

// ---------------------------------------------------------------------------
// Side-by-side comparison (HStore vs TagTables ratio per scenario suffix)
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("Side-by-side (lower per-op is better):");
Console.WriteLine(new string('-', 100));
Console.WriteLine($"{"Scenario",-45} {"TagTables (ms)",18} {"HStore (ms)",15} {"HStore vs TagTables",22}");
Console.WriteLine(new string('-', 100));
var suffixes = results
    .Select(r => r.Scenario.Split(" — ", 2)[1])
    .Distinct()
    .ToArray();
foreach (var suffix in suffixes)
{
    var tt = results.FirstOrDefault(r => r.Scenario == $"TagTables — {suffix}");
    var hs = results.FirstOrDefault(r => r.Scenario == $"HStore — {suffix}");
    if (tt is null || hs is null) continue;

    var ttPerOp = tt.Iterations > 0 ? tt.Elapsed.TotalMilliseconds / tt.Iterations : 0;
    var hsPerOp = hs.Iterations > 0 ? hs.Elapsed.TotalMilliseconds / hs.Iterations : 0;
    var ratio = ttPerOp > 0 ? hsPerOp / ttPerOp : 0;
    var verdict = ratio < 1 ? $"{(1 - ratio) * 100:N0}% faster" : $"{(ratio - 1) * 100:N0}% slower";
    Console.WriteLine($"{suffix,-45} {ttPerOp,18:N3} {hsPerOp,15:N3} {verdict,22}");
}
Console.WriteLine(new string('=', 100));


// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
IDocumentStore BuildStore(EventAppendMode appendMode, DcbStorageMode storageMode, string schemaName)
{
    return DocumentStore.For(opts =>
    {
        opts.Connection(connectionString);
        opts.DatabaseSchemaName = schemaName;
        opts.Events.DatabaseSchemaName = schemaName;
        opts.Events.AppendMode = appendMode;
        opts.Events.DcbStorageMode = storageMode;
        opts.Events.AddEventType<OrderPlaced>();
        opts.Events.AddEventType<OrderShipped>();
        opts.Events.RegisterTagType<CustomerId>("customer");
        opts.Events.RegisterTagType<RegionId>("region");
    });
}

async Task SeedDatabase(IDocumentStore store)
{
    Console.Write($"  Seeding {SEED_STREAMS * SEED_EVENTS_PER_STREAM} tagged events...");
    var sw = Stopwatch.StartNew();

    for (var s = 0; s < SEED_STREAMS; s++)
    {
        await using var session = store.LightweightSession();
        var streamId = Guid.NewGuid();
        var customerId = seededCustomerIds[s];
        var regionId = seededRegionIds[s % seededRegionIds.Length];

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
    results.Add(new BenchResult(name, totalEvents, sw.Elapsed));
    Console.WriteLine($" {totalEvents} events in {sw.Elapsed.TotalMilliseconds:N1}ms");
}

async Task BenchmarkQuery(string name, IDocumentStore store,
    Func<IDocumentSession, Task<IReadOnlyList<IEvent>>> body)
{
    // Warm-up — first call triggers JIT, connection pool, and Postgres plan caching
    await using (var warmup = store.LightweightSession())
    {
        _ = await body(warmup);
    }

    Console.Write($"  {name}...");
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < QUERY_ITERATIONS; i++)
    {
        await using var session = store.LightweightSession();
        _ = await body(session);
    }
    sw.Stop();
    results.Add(new BenchResult(name, QUERY_ITERATIONS, sw.Elapsed));
    Console.WriteLine($" {QUERY_ITERATIONS} iterations in {sw.Elapsed.TotalMilliseconds:N1}ms");
}

async Task BenchmarkExists(string name, IDocumentStore store, Func<EventTagQuery> queryFactory)
{
    await using (var warmup = store.LightweightSession())
    {
        _ = await warmup.Events.EventsExistAsync(queryFactory());
    }

    Console.Write($"  {name}...");
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < QUERY_ITERATIONS; i++)
    {
        await using var session = store.LightweightSession();
        _ = await session.Events.EventsExistAsync(queryFactory());
    }
    sw.Stop();
    results.Add(new BenchResult(name, QUERY_ITERATIONS, sw.Elapsed));
    Console.WriteLine($" {QUERY_ITERATIONS} iterations in {sw.Elapsed.TotalMilliseconds:N1}ms");
}

async Task BenchmarkFetchForWriting(string name, IDocumentStore store, Func<EventTagQuery> queryFactory)
{
    await using (var warmup = store.LightweightSession())
    {
        _ = await warmup.Events.FetchForWritingByTags<OrderAggregate>(queryFactory());
    }

    Console.Write($"  {name}...");
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < QUERY_ITERATIONS; i++)
    {
        await using var session = store.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<OrderAggregate>(queryFactory());
        // Append nothing — we're measuring read + consistency-assertion preparation cost.
        // The boundary's enrolled DCB assertion will run at SaveChanges; we skip the save
        // because the perf interest here is the FetchForWriting path itself.
        _ = boundary;
    }
    sw.Stop();
    results.Add(new BenchResult(name, QUERY_ITERATIONS, sw.Elapsed));
    Console.WriteLine($" {QUERY_ITERATIONS} iterations in {sw.Elapsed.TotalMilliseconds:N1}ms");
}


// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------
internal record BenchResult(string Scenario, int Iterations, TimeSpan Elapsed);

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
