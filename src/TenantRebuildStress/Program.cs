using System.Diagnostics;
using System.Globalization;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using TenantRebuildStress;

// marten#5820: a composite projection rebuilt with one agent per tenant under per-tenant event partitioning, with the
// process's peak physical footprint measured while it runs. See README.md for the options and how to compare runs.

var options = HarnessOptions.Parse(args);
Console.WriteLine($"JasperFx.Events {typeof(IEvent).Assembly.GetName().Version}, {options}");

using var store = HarnessStore.Build(options);

if (options.Seed)
{
    await Seeding.SeedAsync(store, options);
}

if (options.Rebuild)
{
    await Rebuilding.RebuildAsync(store, options);
}

namespace TenantRebuildStress
{
    public sealed record HarnessOptions(
        string ConnectionString,
        string Schema,
        int Tenants,
        int StreamsPerTenant,
        int EventsPerStream,
        int Buckets,
        int BucketsPerEvent,
        int PayloadBytes,
        int BatchSize,
        int Concurrency,
        bool? Serialize,
        bool Seed,
        bool Rebuild)
    {
        public static HarnessOptions Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length - 1; i += 2)
            {
                values[args[i].TrimStart('-')] = args[i + 1];
            }

            string get(string name, string fallback) => values.TryGetValue(name, out var v) ? v : fallback;
            int number(string name, int fallback) => int.Parse(get(name, fallback.ToString()), CultureInfo.InvariantCulture);

            var mode = get("mode", "both");

            return new HarnessOptions(
                get("connection", Environment.GetEnvironmentVariable("marten_testing_database")
                                  ?? "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres")
                + ";Maximum Pool Size=200;Command Timeout=120",
                get("schema", "tenant_rebuild_stress"),
                number("tenants", 24),
                number("streams", 50),
                number("events", 20),
                number("buckets", 4000),
                number("fanout", 40),
                number("payload-kb", 8) * 1024,
                number("batch", 500),
                number("concurrency", 24),
                get("serialize", "unset") switch
                {
                    "on" => true,
                    "off" => false,
                    _ => null
                },
                mode is "seed" or "both",
                mode is "rebuild" or "both");
        }

        public override string ToString() =>
            $"tenants={Tenants}, events/tenant={StreamsPerTenant * EventsPerStream}, buckets/tenant={Buckets}, " +
            $"buckets/event={BucketsPerEvent}, payload={PayloadBytes / 1024}KB, batch={BatchSize}, " +
            $"concurrency={Concurrency}, serialize={(Serialize?.ToString() ?? "unset")}";
    }

    // One bucket per (tenant, bucket id); every event touches BucketsPerEvent of them, so a page of events builds a
    // batch of thousands of large upserts. That makes the memory a built batch holds visible.
    public class StressBucket
    {
        public string Id { get; set; } = "";
        public int Count { get; set; }
        public string Payload { get; set; } = "";
    }

    public class StressStream
    {
        public Guid Id { get; set; }
        public int Count { get; set; }
    }

    public record StressStarted(Guid Id);
    public record Touched(string[] Buckets);

    public partial class BucketProjection: MultiStreamProjection<StressBucket, string>
    {
        public static int PayloadBytes = 8 * 1024;

        public BucketProjection()
        {
            Name = "StressBucket";
            Identities<Touched>(e => e.Buckets);
        }

        public StressBucket Create(Touched e) => new() { Payload = new string('x', PayloadBytes) };

        public void Apply(StressBucket bucket, Touched e) => bucket.Count++;
    }

    public partial class StreamProjection: SingleStreamProjection<StressStream, Guid>
    {
        public StreamProjection()
        {
            Name = "StressStream";
        }

        public void Apply(StressStream stream, Touched e) => stream.Count++;
    }

    public static class HarnessStore
    {
        public const string Composite = "stress-composite";

        public static DocumentStore Build(HarnessOptions options)
        {
            BucketProjection.PayloadBytes = options.PayloadBytes;

            return (DocumentStore)DocumentStore.For(opts =>
            {
                opts.Connection(options.ConnectionString);
                opts.DatabaseSchemaName = options.Schema;
                opts.AutoCreateSchemaObjects = AutoCreate.All;
                opts.Events.TenancyStyle = TenancyStyle.Conjoined;
                opts.Events.UseTenantPartitionedEvents = true;
                opts.Events.AppendMode = EventAppendMode.Quick;
                opts.Policies.AllDocumentsAreMultiTenanted();

                opts.Schema.For<StressBucket>().DocumentAlias("stress_bucket");
                opts.Schema.For<StressStream>().DocumentAlias("stress_stream");

                // One rebuild cell per tenant at once: the many-agents shape of jasperfx#1113
                opts.Projections.MaxConcurrentRebuildsPerDatabase = options.Concurrency;

                if (options.Serialize is { } serialize)
                {
                    // By reflection, so the harness also runs on a JasperFx from before the setting existed
                    var property = opts.Projections.GetType().GetProperty("SerializeTenantProjectionBatches")
                                   ?? throw new InvalidOperationException(
                                       "This JasperFx has no DaemonSettings.SerializeTenantProjectionBatches (jasperfx#1127); run with --serialize unset");
                    property.SetValue(opts.Projections, serialize);
                }

                opts.Projections.CompositeProjectionFor(Composite, c =>
                {
                    c.Options.BatchSize = options.BatchSize;
                    c.Add<BucketProjection>(stageNumber: 1);
                    c.Add<StreamProjection>(stageNumber: 2);
                });
            });
        }

        public static string Tenant(int index) => $"stress_{index:D3}";
    }

    public static class Seeding
    {
        public static async Task SeedAsync(DocumentStore store, HarnessOptions options)
        {
            var watch = Stopwatch.StartNew();

            await store.Advanced.Clean.CompletelyRemoveAllAsync();
            await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

            var tenants = Enumerable.Range(0, options.Tenants).Select(HarnessStore.Tenant).ToArray();
            await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenants);

            await Parallel.ForEachAsync(tenants, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (tenant, ct) =>
            {
                var random = new Random(tenant.GetHashCode());
                await using var session = store.LightweightSession(tenant);

                for (var s = 0; s < options.StreamsPerTenant; s++)
                {
                    var id = Guid.NewGuid();
                    var events = new List<object> { new StressStarted(id) };
                    for (var e = 0; e < options.EventsPerStream; e++)
                    {
                        events.Add(new Touched(Enumerable.Range(0, options.BucketsPerEvent)
                            .Select(_ => $"b{random.Next(options.Buckets)}").Distinct().ToArray()));
                    }

                    session.Events.StartStream<StressStream>(id, events.ToArray());
                }

                await session.SaveChangesAsync(ct);
            });

            Console.WriteLine($"Seeded {tenants.Length} tenants in {watch.Elapsed.TotalSeconds:N1}s");
        }
    }

    public static class Rebuilding
    {
        public static async Task RebuildAsync(DocumentStore store, HarnessOptions options)
        {
            // Settle what start-up allocated, so the peak is the rebuild's
            GC.Collect(2, GCCollectionMode.Forced, true, true);

            using var sampler = FootprintSampler.Start(Environment.ProcessId);
            var watch = Stopwatch.StartNew();

            using (var daemon = await store.BuildProjectionDaemonAsync())
            {
                await daemon.RebuildProjectionAsync(HarnessStore.Composite, tenantId: null, 30.Minutes(),
                    CancellationToken.None);
            }

            var elapsed = watch.Elapsed;
            var peak = sampler.Stop();

            var (buckets, touches) = await countAsync(store, options);

            Console.WriteLine($"Rebuilt in {elapsed.TotalSeconds:N1}s");
            Console.WriteLine($"Peak physical footprint (top MEM): {peak.Mem / 1024.0 / 1024.0:N0} MB, compressed (CMPRS) at that sample: {peak.CmprsAtMemPeak / 1024.0 / 1024.0:N0} MB, peak MEM+CMPRS: {peak.MemPlusCmprs / 1024.0 / 1024.0:N0} MB, {peak.Samples} samples");
            Console.WriteLine($"Buckets projected: {buckets:N0}, touches counted: {touches:N0} (expected {options.Tenants * options.StreamsPerTenant * options.EventsPerStream * options.BucketsPerEvent:N0} at most, fewer where an event drew a bucket twice)");
            Console.WriteLine($"RESULT|{typeof(IEvent).Assembly.GetName().Version}|{options.Serialize?.ToString() ?? "unset"}|{elapsed.TotalSeconds:F1}|{peak.Mem / 1024 / 1024}|{peak.MemPlusCmprs / 1024 / 1024}|{buckets}|{touches}");
        }

        private static async Task<(long buckets, long touches)> countAsync(DocumentStore store, HarnessOptions options)
        {
            long buckets = 0, touches = 0;
            for (var i = 0; i < options.Tenants; i++)
            {
                await using var query = store.QuerySession(HarnessStore.Tenant(i));
                buckets += await query.Query<StressBucket>().CountAsync();
                touches += await query.Query<StressBucket>().SumAsync(x => x.Count);
            }

            return (buckets, touches);
        }
    }

    // Samples the process's physical footprint with macOS `top -stats mem,cmprs` (never RSS) once a second
    public sealed class FootprintSampler : IDisposable
    {
        private readonly Process _top;
        private long _mem;
        private long _cmprsAtMemPeak;
        private long _memPlusCmprs;
        private int _samples;

        private FootprintSampler(Process top)
        {
            _top = top;
            _top.OutputDataReceived += (_, e) => read(e.Data);
            _top.BeginOutputReadLine();
        }

        public static FootprintSampler Start(int pid)
        {
            var top = Process.Start(new ProcessStartInfo("top", $"-l 0 -s 1 -pid {pid} -stats mem,cmprs")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("Could not start top");

            return new FootprintSampler(top);
        }

        private void read(string? line)
        {
            if (line == null) return;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !tryBytes(parts[0], out var mem) || !tryBytes(parts[1], out var cmprs)) return;

            lock (this)
            {
                _samples++;
                if (mem > _mem)
                {
                    _mem = mem;
                    _cmprsAtMemPeak = cmprs;
                }

                _memPlusCmprs = Math.Max(_memPlusCmprs, mem + cmprs);
            }
        }

        private static bool tryBytes(string text, out long bytes)
        {
            bytes = 0;
            text = text.TrimEnd('+', '-');
            if (text.Length < 2) return false;

            var multiplier = char.ToUpperInvariant(text[^1]) switch
            {
                'B' => 1L,
                'K' => 1024L,
                'M' => 1024L * 1024,
                'G' => 1024L * 1024 * 1024,
                _ => 0L
            };

            if (multiplier == 0 || !double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            bytes = (long)(value * multiplier);
            return true;
        }

        public (long Mem, long CmprsAtMemPeak, long MemPlusCmprs, int Samples) Stop()
        {
            // One more second so the last stretch of the rebuild is sampled
            Thread.Sleep(1100);
            if (!_top.HasExited) _top.Kill();
            _top.WaitForExit();

            lock (this) return (_mem, _cmprsAtMemPeak, _memPlusCmprs, _samples);
        }

        public void Dispose()
        {
            if (!_top.HasExited) _top.Kill();
            _top.Dispose();
        }
    }
}
