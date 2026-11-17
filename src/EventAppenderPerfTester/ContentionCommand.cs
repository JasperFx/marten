using System.Collections.Concurrent;
using System.Diagnostics;
using JasperFx;
using JasperFx.CommandLine;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Npgsql;

namespace EventAppenderPerfTester;

// Measurement rig for #5764: opts.Events.UseExclusiveLockOnConcurrentAppends under real
// optimistic-concurrency contention. Run with e.g.
//   dotnet run -c Release -- contention --connection "<conn>" --rounds 4 --writers 32 --streams 4 --seconds 20
public class ContentionInput: NetCoreInput
{
    [FlagAlias("connection", 'c')] public string ConnectionFlag { get; set; } = "";
    public int RoundsFlag { get; set; } = 4;
    public int WritersFlag { get; set; } = 32;
    public int StreamsFlag { get; set; } = 4;
    public int SecondsFlag { get; set; } = 20;
}

public record Ticked;

public class TickCounter
{
    public Guid Id { get; set; }
    public int Ticks { get; set; }
}

public class TickCounterProjection: SingleStreamProjection<TickCounter, Guid>
{
    public void Apply(Ticked e, TickCounter c) => c.Ticks++;
}

public class ContentionCommand: JasperFxAsyncCommand<ContentionInput>
{
    public override async Task<bool> Execute(ContentionInput input)
    {
        if (input.ConnectionFlag.IsEmpty()) throw new ArgumentException("--connection is required");

        Console.WriteLine($"writers={input.WritersFlag} hot streams={input.StreamsFlag} seconds={input.SecondsFlag} rounds={input.RoundsFlag}");
        var results = new List<Result>();
        for (var round = 0; round < input.RoundsFlag; round++)
        {
            // Interleave, and flip the order each round so neither mode owns the "warm" slot.
            var order = round % 2 == 0 ? new[] { false, true } : new[] { true, false };
            foreach (var exclusive in order)
            {
                var r = await run(input, exclusive, round);
                results.Add(r);
                Console.WriteLine(r);
            }
        }

        Console.WriteLine();
        Console.WriteLine("mode | median appends/s | median p50 ms | median p99 ms | median conflicts/s | median gaps | median max lag ms | median drain ms");
        foreach (var group in results.GroupBy(x => x.Exclusive))
        {
            double med(Func<Result, double> f) => group.Select(f).OrderBy(x => x).ElementAt(group.Count() / 2);
            Console.WriteLine($"{(group.Key ? "exclusive" : "default  ")} | {med(x => x.Throughput):F0} | {med(x => x.P50):F1} | {med(x => x.P99):F1} | {med(x => x.ConflictsPerSec):F0} | {med(x => x.Gaps):F0} | {med(x => x.MaxLagMs):F0} | {med(x => x.DrainMs):F0}");
        }

        return true;
    }

    private static async Task<Result> run(ContentionInput input, bool exclusive, int round)
    {
        var schema = "xl_measure";
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(input.ConnectionFlag);
            opts.DatabaseSchemaName = schema;
            opts.DisableNpgsqlLogging = true;
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.UseExclusiveLockOnConcurrentAppends = exclusive;
            opts.Projections.Add<TickCounterProjection>(ProjectionLifecycle.Async);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var ids = Enumerable.Range(0, input.StreamsFlag).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids)
        {
            await using var s = store.LightweightSession();
            s.Events.StartStream<TickCounter>(id, new Ticked());
            await s.SaveChangesAsync();
        }

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var latencies = new ConcurrentBag<double>();
        long ok = 0, conflicts = 0;
        var maxLag = 0L;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(input.SecondsFlag));
        var sw = Stopwatch.StartNew();

        var sampler = Task.Run(async () =>
        {
            await using var conn = new NpgsqlConnection(input.ConnectionFlag);
            await conn.OpenAsync();
            while (!cts.IsCancellationRequested)
            {
                try { await Task.Delay(250, cts.Token); } catch (OperationCanceledException) { break; }
                // lag = how many committed events the async projection has not yet processed
                await using var cmd = new NpgsqlCommand(
                    $"select coalesce((select max(seq_id) from {schema}.mt_events),0) - coalesce((select min(last_seq_id) from {schema}.mt_event_progression where name like '%TickCounter%'),0)", conn);
                var lag = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                Interlocked.Exchange(ref maxLag, Math.Max(Volatile.Read(ref maxLag), lag));
            }
        });

        // Lag is measured in events; convert to ms with the observed rate afterwards.
        var writers = Enumerable.Range(0, input.WritersFlag).Select(w => Task.Run(async () =>
        {
            var rng = new Random(w * 7919 + round);
            while (!cts.IsCancellationRequested)
            {
                var id = ids[rng.Next(ids.Length)];
                var t = Stopwatch.GetTimestamp();
                try
                {
                    await using var s = store.LightweightSession();
                    var stream = await s.Events.FetchForWriting<TickCounter>(id);
                    stream.AppendOne(new Ticked());
                    await s.SaveChangesAsync();
                    Interlocked.Increment(ref ok);
                    latencies.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
                }
                catch (Exception e) when (e.GetType().Name.Contains("UnexpectedMaxEventId")
                                              || e.GetType().Name.Contains("UnexpectedMaxEventId")
                                              || e.InnerException?.GetType().Name.Contains("UnexpectedMaxEventId") == true)
                {
                    Interlocked.Increment(ref conflicts);
                }
                catch (OperationCanceledException) { }
            }
        })).ToArray();

        await Task.WhenAll(writers);
        await sampler;
        sw.Stop();
        var elapsed = sw.Elapsed.TotalSeconds;

        var drain = Stopwatch.StartNew();
        await store.WaitForNonStaleProjectionDataAsync(TimeSpan.FromMinutes(5));
        drain.Stop();
        await daemon.StopAllAsync();

        long gaps;
        await using (var conn = new NpgsqlConnection(input.ConnectionFlag))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"select max(seq_id) - count(*) from {schema}.mt_events", conn);
            gaps = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        var sorted = latencies.OrderBy(x => x).ToArray();
        var rate = ok / elapsed;
        return new Result
        {
            Exclusive = exclusive,
            Throughput = rate,
            P50 = sorted.Length == 0 ? 0 : sorted[sorted.Length / 2],
            P99 = sorted.Length == 0 ? 0 : sorted[(int)(sorted.Length * 0.99)],
            ConflictsPerSec = conflicts / elapsed,
            Gaps = gaps,
            MaxLagMs = rate == 0 ? 0 : maxLag / rate * 1000,
            DrainMs = drain.Elapsed.TotalMilliseconds
        };
    }

    private record Result
    {
        public bool Exclusive { get; init; }
        public double Throughput { get; init; }
        public double P50 { get; init; }
        public double P99 { get; init; }
        public double ConflictsPerSec { get; init; }
        public long Gaps { get; init; }
        public double MaxLagMs { get; init; }
        public double DrainMs { get; init; }

        public override string ToString() =>
            $"{(Exclusive ? "exclusive" : "default  ")} {Throughput:F0}/s p50={P50:F1}ms p99={P99:F1}ms conflicts={ConflictsPerSec:F0}/s gaps={Gaps} maxLag={MaxLagMs:F0}ms drain={DrainMs:F0}ms";
    }
}
