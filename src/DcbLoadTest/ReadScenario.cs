using System.Diagnostics;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;

namespace DcbLoadTest;

/// <summary>
/// 2. FetchForWritingByTags latency against a seeded store, and the full uncontended decision
/// (fetch + append + save) that adds the boundary check to it.
/// </summary>
public static class ReadScenario
{
    private const int EventsPerStream = 10;
    private const int Regions = 1000;

    public static async Task Run(Settings settings)
    {
        var streams = settings.Int("seed-streams", 100_000);
        var iterations = settings.Int("iterations", 1000);

        Report.Line($"## 2. FetchForWritingByTags latency, {streams * EventsPerStream:N0} seeded events");
        Report.Line();
        Report.Header(settings);
        Report.Line($"{streams:N0} customers x {EventsPerStream} events (OrderPlaced/OrderShipped alternating), " +
                    $"each event also tagged with 1 of {Regions} regions (~{streams * EventsPerStream / Regions:N0} events per region). " +
                    $"Seeded under TagValueAndEventType so both kinds of version row exist; {iterations} sequential iterations per row, random customer each time.");
        Report.Line();

        var rows = new List<string[]>();

        foreach (var mode in settings.Modes)
        {
            var schema = $"dcb_read_{mode.ToString().ToLowerInvariant()}_{streams}";
            var customers = await Seed(settings, schema, mode, streams);

            foreach (var scope in settings.Scopes)
            {
                using var store = Stores.Build(settings, schema, mode, scope);
                var random = new Random(42);

                CustomerId AnyCustomer() => customers[random.Next(customers.Length)];

                var queries = new (string Name, Func<EventTagQuery> Query)[]
                {
                    ("1 tag (~10 events)", () => new EventTagQuery().Or(AnyCustomer())),
                    ("1 tag + AndEventsOfType (~5 events)", () => new EventTagQuery().Or(AnyCustomer()).AndEventsOfType<OrderPlaced>()),
                    ("1 tag, no events yet", () => new EventTagQuery().Or(new CustomerId(Guid.NewGuid()))),
                    ($"customer OR region (~{10 + streams * EventsPerStream / Regions:N0} events)",
                        () => new EventTagQuery().Or(AnyCustomer()).Or(new RegionId($"region-{random.Next(Regions)}"))),
                };

                foreach (var (name, query) in queries)
                {
                    var fetch = new Latencies();
                    var decision = new Latencies();
                    long eventsRead = 0;

                    // Warm-up
                    for (var i = 0; i < 25; i++) await Fetch(store, query(), new Latencies());

                    for (var i = 0; i < iterations; i++) eventsRead += await Fetch(store, query(), fetch);
                    for (var i = 0; i < iterations; i++) await Decide(store, query(), decision);

                    rows.Add([
                        mode.ToString(), scope.ToString(), name, (eventsRead / (double)iterations).ToString("N1"),
                        Report.Ms(fetch.Percentile(50)), Report.Ms(fetch.Percentile(99)),
                        Report.Ms(decision.Percentile(50)), Report.Ms(decision.Percentile(99))
                    ]);
                    Report.Progress($"{mode}/{scope} {name}: fetch p50 {fetch.Percentile(50):N2}ms, decision p50 {decision.Percentile(50):N2}ms");
                }
            }
        }

        Report.Table(
            ["Storage", "Scope", "Query", "events read", "fetch p50 ms", "fetch p99 ms", "fetch+append+save p50 ms", "p99 ms"],
            rows);
    }

    private static async Task<int> Fetch(IDocumentStore store, EventTagQuery query, Latencies latency)
    {
        var started = Stopwatch.GetTimestamp();
        await using var session = store.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<OrderAggregate>(query);
        latency.Add(Stopwatch.GetElapsedTime(started));
        return boundary.Events.Count;
    }

    private static async Task Decide(IDocumentStore store, EventTagQuery query, Latencies latency)
    {
        var started = Stopwatch.GetTimestamp();
        await using var session = store.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<OrderAggregate>(query);
        var customer = (CustomerId)query.Conditions[0].TagValue;
        var shipped = session.Events.BuildEvent(new OrderShipped("TRK-decision"));
        shipped.WithTag(customer);
        boundary.AppendOne(shipped);
        await session.SaveChangesAsync();
        latency.Add(Stopwatch.GetElapsedTime(started));
    }

    /// <summary>Seed once per (mode, size); reused when the schema already holds the events.</summary>
    private static async Task<CustomerId[]> Seed(Settings settings, string schema, DcbStorageMode mode, int streams)
    {
        // Customer ids are derived from the stream index so a reused schema still has known customers.
        var customers = Enumerable.Range(0, streams).Select(CustomerFor).ToArray();
        var expected = (long)streams * EventsPerStream;

        long existing = 0;
        try
        {
            existing = await Pg.Scalar(settings, $"select count(*) from {schema}.mt_events");
        }
        catch
        {
            // schema does not exist yet
        }

        if (existing >= expected && settings.Get("reseed", "false") != "true")
        {
            Report.Progress($"{schema}: reusing {existing:N0} seeded events");
            return customers;
        }

        using var store = await Stores.Fresh(settings, schema, mode, DcbConflictScope.TagValueAndEventType);
        var writers = settings.Int("seed-writers", 8);
        const int streamsPerSave = 50;
        var sw = Stopwatch.StartNew();
        var next = -streamsPerSave;

        if (mode == DcbStorageMode.HStore)
        {
            // HStore appends set each event's tags with an UPDATE ... WHERE id = $1, and mt_events has no
            // index on id, so every tagged event costs a sequential scan of the table: seeding through
            // sessions is quadratic (588k of 1M events after 30 minutes). The bulk import writes the hstore
            // column in its COPY instead. It writes no mt_dcb_tag_version rows, which a missing row reads
            // as version 0, the same as an untouched tag.
            var actions = new List<StreamAction>(streams);
            for (var s = 0; s < streams; s++)
            {
                var events = Enumerable.Range(0, EventsPerStream).Select(e => e % 2 == 0
                    ? (object)new OrderPlaced($"ORD-{s}-{e}", 10m + e)
                    : new OrderShipped($"TRK-{s}-{e}")).ToArray();
                var action = StreamAction.Start(store.Events, Guid.NewGuid(), events);
                var e = 0;
                foreach (var @event in action.Events)
                {
                    @event.WithTag(customers[s], new RegionId($"region-{(s * EventsPerStream + e++) % Regions}"));
                }

                actions.Add(action);
            }

            await store.BulkInsertEventsAsync(actions, batchSize: 10_000);
            Report.Progress($"{schema}: bulk-seeded {expected:N0} events in {sw.Elapsed.TotalSeconds:N0}s; vacuum analyze");
            await Pg.Execute(settings, "vacuum analyze");
            return customers;
        }

        await Task.WhenAll(Enumerable.Range(0, writers).Select(_ => Task.Run(async () =>
        {
            while (true)
            {
                var first = Interlocked.Add(ref next, streamsPerSave);
                if (first >= streams) return;

                await using var session = store.LightweightSession();
                for (var s = first; s < Math.Min(first + streamsPerSave, streams); s++)
                {
                    var streamId = Guid.NewGuid();
                    for (var e = 0; e < EventsPerStream; e++)
                    {
                        object data = e % 2 == 0
                            ? new OrderPlaced($"ORD-{s}-{e}", 10m + e)
                            : new OrderShipped($"TRK-{s}-{e}");
                        var wrapped = session.Events.BuildEvent(data);
                        wrapped.WithTag(customers[s], new RegionId($"region-{(s * EventsPerStream + e) % Regions}"));
                        session.Events.Append(streamId, wrapped);
                    }
                }

                await session.SaveChangesAsync();
            }
        })));

        Report.Progress($"{schema}: seeded {expected:N0} events in {sw.Elapsed.TotalSeconds:N0}s; vacuum analyze");
        await Pg.Execute(settings, "vacuum analyze");
        return customers;
    }

    private static CustomerId CustomerFor(int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, index);
        bytes[15] = 0x5e;
        return new CustomerId(new Guid(bytes));
    }
}
